#pragma once
#ifndef BASED_H
#define BASED_H
#include <stdio.h>

#include <map>
#include <list>
#include <queue>
#include <regex>
#include <mutex>
#include <chrono>
#include <string>
#include <vector>
#include <thread>
#include <iostream>

#include <WinSock2.h>
#include <ws2ipdef.h>
#include <WS2tcpip.h>
#include <tlhelp32.h>
#include <mstcpip.h>
#include <Windows.h>

#include <nfapi.h>

using namespace std;

typedef enum _AIO_TYPE {
	AIO_FILTERLOOPBACK,
	AIO_FILTERINTRANET,
	AIO_FILTERPARENT,
	AIO_FILTERICMP,
	AIO_FILTERTCP,
	AIO_FILTERUDP,
	AIO_FILTERDNS,

	AIO_ICMPING,

	AIO_DNSONLY,
	AIO_DNSPROX,
	AIO_DNSHOST,
	AIO_DNSPORT,

	AIO_TGTHOST,
	AIO_TGTPORT,
	AIO_TGTUSER,
	AIO_TGTPASS,

	AIO_CLRNAME,
	AIO_ADDNAME,
	AIO_BYPNAME
} AIO_TYPE;

#pragma pack(push, 8)
typedef struct _NF_STATS {
	uint64_t tcp_connect_total;
	uint32_t tcp_active;
	uint32_t _reserved;
	uint64_t tcp_closed_total;
	uint64_t udp_event_total;
	uint64_t dns_query_total;
	uint64_t dns_failure_total;
	uint64_t redirect_success_total;
	uint64_t redirect_failure_total;
	uint64_t rx_bytes;
	uint64_t tx_bytes;
	uint64_t network_error_total;
} NF_STATS, *PNF_STATS;
#pragma pack(pop)

#include <atomic>
#include <cstdint>

extern std::atomic<uint64_t> g_tcp_connect_total;
extern std::atomic<uint32_t> g_tcp_active;
extern std::atomic<uint64_t> g_tcp_closed_total;
extern std::atomic<uint64_t> g_udp_event_total;
extern std::atomic<uint64_t> g_dns_query_total;
extern std::atomic<uint64_t> g_dns_failure_total;
extern std::atomic<uint64_t> g_redirect_success_total;
extern std::atomic<uint64_t> g_redirect_failure_total;
extern std::atomic<uint64_t> g_rx_bytes;
extern std::atomic<uint64_t> g_tx_bytes;
extern std::atomic<uint64_t> g_network_error_total;

#endif
