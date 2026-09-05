#include "Based.h"
#include "EventHandler.h"
#include "IPEventHandler.h"
#include "Utils.h"

extern bool filterLoopback;
extern bool filterIntranet;
extern bool filterParent;
extern bool filterICMP;
extern bool filterTCP;
extern bool filterUDP;
extern bool filterDNS;

extern DWORD icmping;

extern bool dnsOnly;
extern bool dnsProx;
extern string dnsHost;
extern USHORT dnsPort;

extern wstring tgtHost;
extern wstring tgtPort;
extern string tgtUsername;
extern string tgtPassword;

extern vector<wstring> bypassList;
extern vector<wstring> handleList;

atomic_ullong UP = { 0 };
atomic_ullong DL = { 0 };

std::atomic<uint64_t> g_tcp_connect_total{ 0 };
std::atomic<uint32_t> g_tcp_active{ 0 };
std::atomic<uint64_t> g_tcp_closed_total{ 0 };
std::atomic<uint64_t> g_udp_event_total{ 0 };
std::atomic<uint64_t> g_dns_query_total{ 0 };
std::atomic<uint64_t> g_dns_failure_total{ 0 };
std::atomic<uint64_t> g_redirect_success_total{ 0 };
std::atomic<uint64_t> g_redirect_failure_total{ 0 };
std::atomic<uint64_t> g_rx_bytes{ 0 };
std::atomic<uint64_t> g_tx_bytes{ 0 };
std::atomic<uint64_t> g_network_error_total{ 0 };

NF_EventHandler EventHandler = {
	threadStart,
	threadEnd,
	tcpConnectRequest,
	tcpConnected,
	tcpClosed,
	tcpReceive,
	tcpSend,
	tcpCanReceive,
	tcpCanSend,
	udpCreated,
	udpConnectRequest,
	udpClosed,
	udpReceive,
	udpSend,
	udpCanReceive,
	udpCanSend
};

NF_IPEventHandler IPEventHandler = {
	ipReceive,
	ipSend
};

BOOL APIENTRY DllMain(HMODULE hModule, DWORD dwReason, LPVOID lpReserved)
{
	UNREFERENCED_PARAMETER(hModule);
	UNREFERENCED_PARAMETER(dwReason);
	UNREFERENCED_PARAMETER(lpReserved);

	return TRUE;
}

extern "C" {
	__declspec(dllexport) void __cdecl aio_getStats(NF_STATS* stats);
	__declspec(dllexport) void __cdecl aio_resetStats();

	__declspec(dllexport) BOOL __cdecl aio_register(LPWSTR value)
	{
		auto status = nf_registerDriver(ws2s(value).c_str());
		if (status != NF_STATUS_SUCCESS)
		{
			printf("[Redirector][aio_register] nf_registerDriver: %d\n", status);
			return FALSE;
		}

		return TRUE;
	}

	__declspec(dllexport) BOOL __cdecl aio_unregister(LPWSTR value)
	{
		auto status = nf_unRegisterDriver(ws2s(value).c_str());
		if (status != NF_STATUS_SUCCESS)
		{
			printf("[Redirector][aio_unregister] nf_unRegisterDriver: %d\n", status);
			return FALSE;
		}

		return TRUE;
	}

	__declspec(dllexport) BOOL __cdecl aio_dial(int name, LPWSTR value)
	{
		switch (name)
		{
		case AIO_FILTERLOOPBACK:
			filterLoopback = (wstring(value).find(L"false") == string::npos);
			break;
		case AIO_FILTERINTRANET:
			filterIntranet = (wstring(value).find(L"false") == string::npos);
			break;
		case AIO_FILTERPARENT:
			filterParent = (wstring(value).find(L"false") == string::npos);
			break;
		case AIO_FILTERICMP:
			filterICMP = (wstring(value).find(L"false") == string::npos);
			break;
		case AIO_FILTERTCP:
			filterTCP = (wstring(value).find(L"false") == string::npos);
			break;
		case AIO_FILTERUDP:
			filterUDP = (wstring(value).find(L"false") == string::npos);
			break;
		case AIO_FILTERDNS:
			filterDNS = (wstring(value).find(L"false") == string::npos);
			break;
		case AIO_ICMPING:
			icmping = atoi(ws2s(value).c_str());
			break;
		case AIO_DNSONLY:
			dnsOnly = (wstring(value).find(L"false") == string::npos);
			break;
		case AIO_DNSPROX:
			dnsProx = (wstring(value).find(L"false") == string::npos);
			break;
		case AIO_DNSHOST:
			dnsHost = ws2s(value);
			break;
		case AIO_DNSPORT:
			dnsPort = static_cast<USHORT>(atoi(ws2s(value).c_str()));
			break;
		case AIO_TGTHOST:
			tgtHost = wstring(value);
			break;
		case AIO_TGTPORT:
			tgtPort = wstring(value);
			break;
		case AIO_TGTUSER:
			tgtUsername = ws2s(value);
			break;
		case AIO_TGTPASS:
			tgtPassword = ws2s(value);
			break;
		case AIO_CLRNAME:
			bypassList.clear();
			handleList.clear();
			break;
		case AIO_BYPNAME:
			try
			{
				std::wregex checker(value);
			}
			catch (regex_error) {
				return FALSE;
			}

			bypassList.emplace_back(value);
			break;
		case AIO_ADDNAME:
			try
			{
				std::wregex checker(value);
			}
			catch (regex_error) {
				return FALSE;
			}

			handleList.emplace_back(value);
			break;
		default:
			return FALSE;
		}

		return TRUE;
	}

	__declspec(dllexport) BOOL __cdecl aio_init()
	{
		aio_resetStats();

		WSADATA data;
		if (WSAStartup(MAKEWORD(2, 2), &data) != NO_ERROR)
		{
			puts("[Redirector][aio_init] WSAStartup != NO_ERROR");
			return FALSE;
		}

		nf_adjustProcessPriviledges();
		if (!eh_init())
		{
			puts("[Redirector][aio_init] !eh_init");
			return FALSE;
		}

		if (nf_init("netfilter2", &EventHandler) != NF_STATUS_SUCCESS)
		{
			puts("[Redirector][aio_init] nf_init != NF_STATUS_SUCCESS");
			return FALSE;
		}

		NF_RULE rule;
		if (!filterLoopback)
		{
			memset(&rule, 0, sizeof(NF_RULE));
			rule.ip_family = AF_INET;
			inet_pton(AF_INET, "127.0.0.1", rule.remoteIpAddress);
			inet_pton(AF_INET, "255.0.0.0", rule.remoteIpAddressMask);
			rule.filteringFlag = NF_ALLOW;
			nf_addRule(&rule, FALSE);

			memset(&rule, 0, sizeof(NF_RULE));
			rule.ip_family = AF_INET6;
			rule.remoteIpAddress[15] = 1;
			memset(rule.remoteIpAddressMask, 0xff, sizeof(rule.remoteIpAddressMask));
			rule.filteringFlag = NF_ALLOW;
			nf_addRule(&rule, FALSE);
		}

		if (!filterIntranet)
		{
			/* 10.0.0.0/8 */
			memset(&rule, 0, sizeof(NF_RULE));
			rule.ip_family = AF_INET;
			inet_pton(AF_INET, "10.0.0.0", rule.remoteIpAddress);
			inet_pton(AF_INET, "255.0.0.0", rule.remoteIpAddressMask);
			rule.filteringFlag = NF_ALLOW;
			nf_addRule(&rule, FALSE);

			/* 100.64.0.0/10 */
			memset(&rule, 0, sizeof(NF_RULE));
			rule.ip_family = AF_INET;
			inet_pton(AF_INET, "100.64.0.0", rule.remoteIpAddress);
			inet_pton(AF_INET, "255.192.0.0", rule.remoteIpAddressMask);
			rule.filteringFlag = NF_ALLOW;
			nf_addRule(&rule, FALSE);

			/* 169.254.0.0/16 */
			memset(&rule, 0, sizeof(NF_RULE));
			rule.ip_family = AF_INET;
			inet_pton(AF_INET, "169.254.0.0", rule.remoteIpAddress);
			inet_pton(AF_INET, "255.255.0.0", rule.remoteIpAddressMask);
			rule.filteringFlag = NF_ALLOW;
			nf_addRule(&rule, FALSE);

			/* 172.16.0.0/12 */
			memset(&rule, 0, sizeof(NF_RULE));
			rule.ip_family = AF_INET;
			inet_pton(AF_INET, "100.64.0.0", rule.remoteIpAddress);
			inet_pton(AF_INET, "255.240.0.0", rule.remoteIpAddressMask);
			rule.filteringFlag = NF_ALLOW;
			nf_addRule(&rule, FALSE);

			/* 192.0.0.0/24 */
			memset(&rule, 0, sizeof(NF_RULE));
			rule.ip_family = AF_INET;
			inet_pton(AF_INET, "192.0.0.0", rule.remoteIpAddress);
			inet_pton(AF_INET, "255.255.255.0", rule.remoteIpAddressMask);
			rule.filteringFlag = NF_ALLOW;
			nf_addRule(&rule, FALSE);

			/* 192.168.0.0/16 */
			memset(&rule, 0, sizeof(NF_RULE));
			rule.ip_family = AF_INET;
			inet_pton(AF_INET, "192.168.0.0", rule.remoteIpAddress);
			inet_pton(AF_INET, "255.255.0.0", rule.remoteIpAddressMask);
			rule.filteringFlag = NF_ALLOW;
			nf_addRule(&rule, FALSE);

			/* 198.18.0.0/15 */
			memset(&rule, 0, sizeof(NF_RULE));
			rule.ip_family = AF_INET;
			inet_pton(AF_INET, "198.18.0.0", rule.remoteIpAddress);
			inet_pton(AF_INET, "255.254.0.0", rule.remoteIpAddressMask);
			rule.filteringFlag = NF_ALLOW;
			nf_addRule(&rule, FALSE);
		}

		if (filterICMP)
		{
			nf_setIPEventHandler(&IPEventHandler);

			memset(&rule, 0, sizeof(NF_RULE));
			rule.ip_family = AF_INET;
			rule.protocol = IPPROTO_ICMP;
			rule.direction = NF_D_OUT;
			rule.filteringFlag = NF_FILTER_AS_IP_PACKETS;
			nf_addRule(&rule, FALSE);
		}

		if (filterTCP)
		{
			memset(&rule, 0, sizeof(NF_RULE));
			rule.ip_family = AF_INET;
			rule.protocol = IPPROTO_TCP;
			rule.direction = NF_D_OUT;
			rule.filteringFlag = NF_INDICATE_CONNECT_REQUESTS;
			nf_addRule(&rule, FALSE);

			memset(&rule, 0, sizeof(NF_RULE));
			rule.ip_family = AF_INET6;
			rule.protocol = IPPROTO_TCP;
			rule.direction = NF_D_OUT;
			rule.filteringFlag = NF_INDICATE_CONNECT_REQUESTS;
			nf_addRule(&rule, FALSE);
		}

		if (filterUDP || filterDNS)
		{
			memset(&rule, 0, sizeof(NF_RULE));
			rule.ip_family = AF_INET;
			rule.protocol = IPPROTO_UDP;
			rule.filteringFlag = NF_FILTER;
			nf_addRule(&rule, FALSE);

			memset(&rule, 0, sizeof(NF_RULE));
			rule.ip_family = AF_INET6;
			rule.protocol = IPPROTO_UDP;
			rule.filteringFlag = NF_FILTER;
			nf_addRule(&rule, FALSE);
		}

		return TRUE;
	}

	__declspec(dllexport) void __cdecl aio_free()
	{
		nf_deleteRules();
		nf_free();
		eh_free();

		WSACleanup();
		return;
	}

	__declspec(dllexport) ULONG64 __cdecl aio_getUP()
	{
		return UP;
	}

	__declspec(dllexport) ULONG64 __cdecl aio_getDL()
	{
		return DL;
	}

	__declspec(dllexport) void __cdecl aio_getStats(NF_STATS* stats)
	{
		if (!stats) return;
		stats->tcp_connect_total      = g_tcp_connect_total.load(std::memory_order_relaxed);
		stats->tcp_active             = g_tcp_active.load(std::memory_order_relaxed);
		stats->_reserved              = 0;
		stats->tcp_closed_total       = g_tcp_closed_total.load(std::memory_order_relaxed);
		stats->udp_event_total        = g_udp_event_total.load(std::memory_order_relaxed);
		stats->dns_query_total        = g_dns_query_total.load(std::memory_order_relaxed);
		stats->dns_failure_total      = g_dns_failure_total.load(std::memory_order_relaxed);
		stats->redirect_success_total = g_redirect_success_total.load(std::memory_order_relaxed);
		stats->redirect_failure_total = g_redirect_failure_total.load(std::memory_order_relaxed);
		stats->rx_bytes               = g_rx_bytes.load(std::memory_order_relaxed);
		stats->tx_bytes               = g_tx_bytes.load(std::memory_order_relaxed);
		stats->network_error_total    = g_network_error_total.load(std::memory_order_relaxed);
	}

	__declspec(dllexport) void __cdecl aio_resetStats()
	{
		g_tcp_connect_total.store(0, std::memory_order_relaxed);
		g_tcp_active.store(0, std::memory_order_relaxed);
		g_tcp_closed_total.store(0, std::memory_order_relaxed);
		g_udp_event_total.store(0, std::memory_order_relaxed);
		g_dns_query_total.store(0, std::memory_order_relaxed);
		g_dns_failure_total.store(0, std::memory_order_relaxed);
		g_redirect_success_total.store(0, std::memory_order_relaxed);
		g_redirect_failure_total.store(0, std::memory_order_relaxed);
		g_rx_bytes.store(0, std::memory_order_relaxed);
		g_tx_bytes.store(0, std::memory_order_relaxed);
		g_network_error_total.store(0, std::memory_order_relaxed);
	}
}
