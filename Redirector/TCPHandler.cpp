#include "TCPHandler.h"

SOCKET tcpSocket = INVALID_SOCKET;
USHORT tcpListen = 0;

mutex tcpLock;
map<USHORT, SOCKADDR_IN6> tcpContext;

bool TCPHandler::INIT()
{
	auto lg = lock_guard<mutex>(tcpLock);

	if (tcpSocket != INVALID_SOCKET)
	{
		closesocket(tcpSocket);

		tcpSocket = INVALID_SOCKET;
	}

	auto client = socket(AF_INET6, SOCK_STREAM, IPPROTO_TCP);
	if (client == INVALID_SOCKET)
	{
		printf("[Redirector][TCPHandler::INIT] Create socket failed: %d\n", WSAGetLastError());
		return false;
	}

	{
		int v6only = 0;
		if (setsockopt(client, IPPROTO_IPV6, IPV6_V6ONLY, (char*)&v6only, sizeof(v6only)) == SOCKET_ERROR)
		{
			printf("[Redirector][TCPHandler::INIT] Set socket option failed: %d\n", WSAGetLastError());

			closesocket(client);
			return false;
		}
	}

	{
		SOCKADDR_IN6 addr;
		IN6ADDR_SETANY(&addr);

		if (bind(client, (PSOCKADDR)&addr, sizeof(SOCKADDR_IN6)) == SOCKET_ERROR)
		{
			printf("[Redirector][TCPHandler::INIT] Bind socket failed: %d\n", WSAGetLastError());

			closesocket(client);
			return false;
		}
	}

	if (listen(client, 1024) == SOCKET_ERROR)
	{
		printf("[Redirector][TCPHandler::INIT] Listen socket failed: %d\n", WSAGetLastError());

		closesocket(client);
		return false;
	}

	{
		SOCKADDR_IN6 addr;
		int addrLength = sizeof(SOCKADDR_IN6);
		if (getsockname(client, (PSOCKADDR)&addr, &addrLength) == SOCKET_ERROR)
		{
			printf("[Redirector][TCPHandler::INIT] Get listen address failed: %d\n", WSAGetLastError());

			closesocket(client);
			return false;
		}

		tcpListen = (addr.sin6_family == AF_INET6) ? addr.sin6_port : ((PSOCKADDR_IN)&addr)->sin_port;
	}

	tcpSocket = client;

	thread(TCPHandler::Accept).detach();
	return true;
}

void TCPHandler::FREE()
{
	auto lg = lock_guard<mutex>(tcpLock);

	if (tcpSocket != INVALID_SOCKET)
	{
		closesocket(tcpSocket);

		tcpSocket = INVALID_SOCKET;
	}
	tcpListen = 0;

	tcpContext.clear();
}

void TCPHandler::CreateHandler(SOCKADDR_IN6 client, SOCKADDR_IN6 remote)
{
	auto lg = lock_guard<mutex>(tcpLock);

	auto id = (client.sin6_family == AF_INET) ? ((PSOCKADDR_IN)&client)->sin_port : client.sin6_port;
	if (tcpContext.find(id) != tcpContext.end())
		tcpContext.erase(id);

	tcpContext[id] = remote;
}

void TCPHandler::DeleteHandler(SOCKADDR_IN6 client)
{
	auto lg = lock_guard<mutex>(tcpLock);

	auto id = (client.sin6_family == AF_INET) ? ((PSOCKADDR_IN)&client)->sin_port : client.sin6_port;
	if (tcpContext.find(id) != tcpContext.end())
		tcpContext.erase(id);
}

void TCPHandler::Accept()
{
	while (tcpSocket != INVALID_SOCKET)
	{
		auto client = accept(tcpSocket, NULL, NULL);
		if (client == INVALID_SOCKET)
		{
			int lasterr = WSAGetLastError();
			if (lasterr == 10004)
				return;

			printf("[Redirector][TCPHandler::Accept] Accept client failed: %d\n", lasterr);
			return;
		}

		thread(TCPHandler::Handle, client).detach();
	}
}

void TCPHandler::Handle(SOCKET client)
{
	USHORT id = 0;

	{
		SOCKADDR_IN6 addr;
		int addrLength = sizeof(SOCKADDR_IN6);

		if (getpeername(client, (PSOCKADDR)&addr, &addrLength) == SOCKET_ERROR)
		{
			g_network_error_total.fetch_add(1, std::memory_order_relaxed);
			g_redirect_failure_total.fetch_add(1, std::memory_order_relaxed);
			closesocket(client);
			return;
		}

		id = (addr.sin6_family == AF_INET) ? ((PSOCKADDR_IN)&addr)->sin_port : addr.sin6_port;
	}

	tcpLock.lock();
	if (tcpContext.find(id) == tcpContext.end())
	{
		tcpLock.unlock();

		g_redirect_failure_total.fetch_add(1, std::memory_order_relaxed);
		closesocket(client);
		return;
	}

	auto target = tcpContext[id];
	tcpLock.unlock();

	auto remote = new SocksHelper::TCP();
	if (!remote->Connect(&target))
	{
		g_redirect_failure_total.fetch_add(1, std::memory_order_relaxed);
		closesocket(client);

		delete remote;
		return;
	}

	g_redirect_success_total.fetch_add(1, std::memory_order_relaxed);
	g_tcp_active.fetch_add(1, std::memory_order_relaxed);

	thread(TCPHandler::Send, client, remote).detach();
	TCPHandler::Read(client, remote);

	g_tcp_active.fetch_sub(1, std::memory_order_relaxed);
	g_tcp_closed_total.fetch_add(1, std::memory_order_relaxed);

	closesocket(client);
	delete remote;
}

void TCPHandler::Read(SOCKET client, SocksHelper::PTCP remote)
{
	char buffer[1446];

	while (tcpSocket != INVALID_SOCKET)
	{
		int length = remote->Read(buffer, sizeof(buffer));
		if (length == 0 || length == SOCKET_ERROR)
		{
			if (length == SOCKET_ERROR)
			{
				int err = WSAGetLastError();
				if (err != WSAEINTR && err != 0)
					g_network_error_total.fetch_add(1, std::memory_order_relaxed);
			}
			return;
		}

		int sent = send(client, buffer, length, 0);
		if (sent == SOCKET_ERROR)
		{
			int err = WSAGetLastError();
			if (err != WSAEINTR && err != 0)
				g_network_error_total.fetch_add(1, std::memory_order_relaxed);
			return;
		}
		if (sent > 0)
		{
			g_rx_bytes.fetch_add(sent, std::memory_order_relaxed);
		}
		if (sent != length)
			return;
	}
}

void TCPHandler::Send(SOCKET client, SocksHelper::PTCP remote)
{
	char buffer[1446];

	while (tcpSocket != INVALID_SOCKET)
	{
		int length = recv(client, buffer, sizeof(buffer), 0);
		if (length == 0 || length == SOCKET_ERROR)
		{
			if (length == SOCKET_ERROR)
			{
				int err = WSAGetLastError();
				if (err != WSAEINTR && err != 0)
					g_network_error_total.fetch_add(1, std::memory_order_relaxed);
			}
			return;
		}

		int sent = remote->Send(buffer, length);
		if (sent == SOCKET_ERROR)
		{
			int err = WSAGetLastError();
			if (err != WSAEINTR && err != 0)
				g_network_error_total.fetch_add(1, std::memory_order_relaxed);
			return;
		}
		if (sent > 0)
		{
			g_tx_bytes.fetch_add(sent, std::memory_order_relaxed);
		}
		if (sent != length)
			return;
	}
}
