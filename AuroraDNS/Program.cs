﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ARSoft.Tools.Net;
using ARSoft.Tools.Net.Dns;
using MojoUnity;
using static System.Net.ServicePointManager;

// ReSharper disable UnusedParameter.Local
#pragma warning disable 1998
#pragma warning disable 169

namespace AuroraDNS
{

    static class Program
    {
        private static IPAddress IntIPAddr;
        private static IPAddress LocIPAddr;
        private static ConsoleColor OriginColor;
        private static List<DomainName> BlackList;
        private static List<DomainName> ChinaList;
        private static Dictionary<DomainName, IPAddress> WhiteList;

        public static class ADnsSetting
        {

            public static string HttpsDnsUrl = "https://1.0.0.1/dns-query";
            //"https://dns.google.com/resolve";
            //"https://dnsp.mili.one:23233/dns-query";
            //"https://dnsp1.mili.one:23233/dns-query";
            //"https://plus1s.site/extdomains/dns.google.com/resolve";

            public static IPAddress ListenIp = IPAddress.Any;
            public static IPAddress EDnsIp = IPAddress.Any;
            public static bool EDnsCustomize;
            public static bool ProxyEnable;
            public static bool IPv6Enable = true;
            public static bool DebugLog;
            public static bool BlackListEnable;
            public static bool ChinaListEnable;
            public static bool WhiteListEnable;
            public static bool AllowSelfSignedCert;
            public static WebProxy WProxy = new WebProxy("127.0.0.1:1080");
        }

        static void Main(string[] args)
        {
            Console.WriteLine(Resource.ASCII);

            OriginColor = Console.ForegroundColor;
            LocIPAddr = IPAddress.Parse(GetLocIp());

            SecurityProtocol = SecurityProtocolType.Tls12;
            if (ADnsSetting.AllowSelfSignedCert)
            {
                ServerCertificateValidationCallback +=
                    (sender, cert, chain, sslPolicyErrors) => true;
            }

            if (Thread.CurrentThread.CurrentCulture.Name == "zh-CN")
            {
                IntIPAddr = IPAddress.Parse(new WebClient().DownloadString("http://members.3322.org/dyndns/getip").Trim());
            }
            else
            {
                IntIPAddr = IPAddress.Parse(new WebClient().DownloadString("https://api.ipify.org").Trim());
            }

            Console.Clear();

            if (!string.IsNullOrWhiteSpace(string.Join("",args)))
                ReadConfig(args[0]);
            if (File.Exists("config.json"))
                ReadConfig("config.json");

            if (ADnsSetting.BlackListEnable)
            {
                string[] blackListStrs = File.ReadAllLines("black.list");

                BlackList = Array.ConvertAll(blackListStrs, DomainName.Parse).ToList();

                if (ADnsSetting.DebugLog)
                {
                    Console.WriteLine(@"-------Black List-------");
                    foreach (var itemName in BlackList)
                    {
                        Console.WriteLine(itemName.ToString());
                    }
                }
            }

            if (ADnsSetting.ChinaListEnable)
            {
                string[] chinaListStrs = File.ReadAllLines("china.list");

                ChinaList = Array.ConvertAll(chinaListStrs, DomainName.Parse).ToList();

                if (ADnsSetting.DebugLog)
                {
                    Console.WriteLine(@"-------China List-------");
                    foreach (var itemName in ChinaList)
                    {
                        Console.WriteLine(itemName.ToString());
                    }
                }
            }

            if (ADnsSetting.WhiteListEnable)
            {
                string[] whiteListStrs;
                if (File.Exists("white.list"))
                    whiteListStrs = File.ReadAllLines("white.list");
                else
                    whiteListStrs = File.ReadAllLines("rewrite.list");

                WhiteList = whiteListStrs.Select(
                    itemStr => itemStr.Split(' ', ',', '\t')).ToDictionary(
                    whiteSplit => DomainName.Parse(whiteSplit[1]),
                    whiteSplit => IPAddress.Parse(whiteSplit[0]));

                if (ADnsSetting.DebugLog)
                {
                    Console.WriteLine(@"-------White List-------");
                    foreach (var itemName in WhiteList)
                    {
                        Console.WriteLine(itemName.Key + @" : " + itemName.Value);
                    }
                }
            }

            using (DnsServer dnsServer = new DnsServer(ADnsSetting.ListenIp, 10, 10))
            {
                dnsServer.QueryReceived += ServerOnQueryReceived;
                dnsServer.Start();
                Console.WriteLine(@"-------AURORA DNS-------");

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(DateTime.Now);
                Console.WriteLine(@"AuroraDNS Server Running");
                Console.ForegroundColor = OriginColor;

                Console.WriteLine(@"Press any key to stop dns server");
                Console.WriteLine(Resource.Line);
                Console.ReadKey();
            }
        }

        private static async Task ServerOnQueryReceived(object sender, QueryReceivedEventArgs e)
        {
            if (!(e.Query is DnsMessage query))
                return;

            IPAddress clientAddress = e.RemoteEndpoint.Address;
            if (ADnsSetting.EDnsCustomize)
                if (Equals(ADnsSetting.EDnsIp, IPAddress.Parse("0.0.0.1")))
                {
                    clientAddress = IPAddress.Parse(IntIPAddr.ToString().Substring(0,
                                                        IntIPAddr.ToString().LastIndexOf(".", StringComparison.Ordinal)) + ".0");
                }
                else
                {
                    clientAddress = ADnsSetting.EDnsIp;
                }
            else if (Equals(clientAddress, IPAddress.Loopback))
                clientAddress = IntIPAddr;
            else if (InSameLaNet(clientAddress, LocIPAddr) && !Equals(IntIPAddr, LocIPAddr))
                clientAddress = IntIPAddr;

            DnsMessage response = query.CreateResponseInstance();

            try
            {
                if (query.Questions.Count <= 0)
                    response.ReturnCode = ReturnCode.ServerFailure;
                else
                {
                    foreach (DnsQuestion dnsQuestion in query.Questions)
                    {
                        response.ReturnCode = ReturnCode.NoError;
                        if (ADnsSetting.DebugLog)
                        {
                            Console.WriteLine(
                                $@"| {DateTime.Now} {clientAddress} : {dnsQuestion.Name} | {dnsQuestion.RecordType.ToString().ToUpper()}");
                        }

                        if (ADnsSetting.BlackListEnable && BlackList.Contains(dnsQuestion.Name)
                                                        && dnsQuestion.RecordType == RecordType.A)
                        {
                            if (ADnsSetting.DebugLog)
                            {
                                Console.WriteLine(@"|- BlackList");
                            }

                            //BlackList
                            response.ReturnCode = ReturnCode.NxDomain;
                            //response.AnswerRecords.Add(new ARecord(dnsQuestion.Name, 10, IPAddress.Any));
                        }

                        if (ADnsSetting.ChinaListEnable && dnsQuestion.RecordType == RecordType.A)
                        {
                            if (ChinaList.Contains(dnsQuestion.Name) || dnsQuestion.Name.ToString().Contains(".cn") || dnsQuestion.Name.ToString().Contains(".xn--"))
                            {
                                var resolvedDnsList = ResolveOverDNSPod(dnsQuestion.Name.ToString());

                                if (resolvedDnsList != null && resolvedDnsList != new List<dynamic>())
                                {
                                    foreach (var item in resolvedDnsList)
                                    {
                                        response.AnswerRecords.Add(item);
                                    }
                                }
                                else
                                {
                                    response.ReturnCode = ReturnCode.NxDomain;
                                }

                                Console.WriteLine(@"|- ChinaList - DNSPOD");
                            }
                        }

                        else if (ADnsSetting.WhiteListEnable && WhiteList.ContainsKey(dnsQuestion.Name)
                                                             && dnsQuestion.RecordType == RecordType.A)
                        {
                            if (ADnsSetting.DebugLog)
                            {
                                Console.WriteLine(@"|- WhiteList");
                            }

                            //WhiteList
                            ARecord blackRecord = new ARecord(dnsQuestion.Name, 10, WhiteList[dnsQuestion.Name]);
                            response.AnswerRecords.Add(blackRecord);
                        }

                        else
                        {
                            //Resolve
                            var (resolvedDnsList, statusCode) = ResolveOverHttps(clientAddress.ToString(),
                                dnsQuestion.Name.ToString(),
                                ADnsSetting.ProxyEnable, ADnsSetting.WProxy, dnsQuestion.RecordType);

                            if (resolvedDnsList != null && resolvedDnsList != new List<dynamic>())
                            {
                                foreach (var item in resolvedDnsList)
                                {
                                    response.AnswerRecords.Add(item);
                                }
                            }
                            else
                            {
                                response.ReturnCode = (ReturnCode) statusCode;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                response.ReturnCode = ReturnCode.ServerFailure;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(@"| " + ex);
                Console.ForegroundColor = OriginColor;
            }

            e.Response = response;

        }

        private static (List<dynamic> list, int statusCode) ResolveOverHttps(string clientIpAddress, string domainName,
            bool proxyEnable = false, IWebProxy wProxy = null, RecordType type = RecordType.A)
        {
            string dnsStr;
            List<dynamic> recordList = new List<dynamic>();

            using (WebClient webClient = new WebClient())
            {
                if (proxyEnable)
                    webClient.Proxy = wProxy;

                dnsStr = webClient.DownloadString(
                    ADnsSetting.HttpsDnsUrl +
                    @"?ct=application/dns-json&" +
                    $"name={domainName}&type={type.ToString().ToUpper()}&edns_client_subnet={clientIpAddress}");
            }

            JsonValue dnsJsonValue = Json.Parse(dnsStr);

            int statusCode = dnsJsonValue.AsObjectGetInt("Status");
            if (statusCode != 0)
                return (new List<dynamic>(), statusCode);

            if (dnsStr.Contains("\"Answer\""))
            {
                var dnsAnswerJsonList = dnsJsonValue.AsObjectGetArray("Answer");

                foreach (var itemJsonValue in dnsAnswerJsonList)
                {
                    string answerAddr = itemJsonValue.AsObjectGetString("data");
                    string answerDomainName = itemJsonValue.AsObjectGetString("name");
                    int answerType = itemJsonValue.AsObjectGetInt("type");
                    int ttl = itemJsonValue.AsObjectGetInt("TTL");

                    if (type == RecordType.A)
                    {
                        if (Convert.ToInt32(RecordType.A) == answerType)
                        {
                            ARecord aRecord = new ARecord(
                                DomainName.Parse(answerDomainName), ttl, IPAddress.Parse(answerAddr));

                            recordList.Add(aRecord);
                        }
                        else if (Convert.ToInt32(RecordType.CName) == answerType)
                        {
                            CNameRecord cRecord = new CNameRecord(
                                DomainName.Parse(answerDomainName), ttl, DomainName.Parse(answerAddr));

                            recordList.Add(cRecord);

                            //recordList.AddRange(ResolveOverHttps(clientIpAddress,answerAddr));
                            //return recordList;
                        }
                    }
                    else if (type == RecordType.Aaaa && ADnsSetting.IPv6Enable)
                    {
                        if (Convert.ToInt32(RecordType.Aaaa) == answerType)
                        {
                            AaaaRecord aaaaRecord = new AaaaRecord(
                                DomainName.Parse(answerDomainName), ttl, IPAddress.Parse(answerAddr));
                            recordList.Add(aaaaRecord);
                        }
                        else if (Convert.ToInt32(RecordType.CName) == answerType)
                        {
                            CNameRecord cRecord = new CNameRecord(
                                DomainName.Parse(answerDomainName), ttl, DomainName.Parse(answerAddr));
                            recordList.Add(cRecord);
                        }
                    }
                    else if (type == RecordType.CName && answerType == Convert.ToInt32(RecordType.CName))
                    {
                        CNameRecord cRecord = new CNameRecord(
                            DomainName.Parse(answerDomainName), ttl, DomainName.Parse(answerAddr));
                        recordList.Add(cRecord);
                    }
                    else if (type == RecordType.Ns && answerType == Convert.ToInt32(RecordType.Ns))
                    {
                        NsRecord nsRecord = new NsRecord(
                            DomainName.Parse(answerDomainName), ttl, DomainName.Parse(answerAddr));
                        recordList.Add(nsRecord);
                    }
                    else if (type == RecordType.Mx && answerType == Convert.ToInt32(RecordType.Mx))
                    {
                        MxRecord mxRecord = new MxRecord(
                            DomainName.Parse(answerDomainName), ttl, 
                            ushort.Parse(answerAddr.Split(' ')[0]),
                            DomainName.Parse(answerAddr.Split(' ')[1]));
                        recordList.Add(mxRecord);
                    }
                    else if (type == RecordType.Txt && answerType == Convert.ToInt32(RecordType.Txt))
                    {
                        TxtRecord txtRecord = new TxtRecord(DomainName.Parse(answerDomainName), ttl, answerAddr);
                        recordList.Add(txtRecord);
                    }
                    else if (type == RecordType.Ptr && answerType == Convert.ToInt32(RecordType.Ptr))
                    {
                        PtrRecord ptrRecord = new PtrRecord(
                            DomainName.Parse(answerDomainName), ttl, DomainName.Parse(answerAddr));
                        recordList.Add(ptrRecord);
                    }
                }
            }

            return (recordList, statusCode);
        }

        public static List<dynamic> ResolveOverDNSPod(string domainName)
        {
            List<dynamic> recordList = new List<dynamic>();

            string dnsStr = new WebClient().DownloadString(
                $"http://119.29.29.29/d?dn={domainName}");
            var dnsAnswerList = dnsStr.Split(';');

            foreach (var item in dnsAnswerList)
            {
                ARecord aRecord = new ARecord(
                    DomainName.Parse(domainName), 600, IPAddress.Parse(item));

                recordList.Add(aRecord);
            }

            return recordList;
        }

        private static bool InSameLaNet(IPAddress ipA, IPAddress ipB)
        {
            return ipA.GetHashCode() % 65536L == ipB.GetHashCode() % 65536L;
        }

        private static string GetLocIp()
        {
            try
            {
                using (TcpClient tcpClient = new TcpClient())
                {
                    tcpClient.Connect("www.sjtu.edu.cn", 80);
                    return ((IPEndPoint) tcpClient.Client.LocalEndPoint).Address.ToString();
                }
            }
            catch (Exception)
            {
                return "192.168.0.1";
            }
        }

        private static void ReadConfig(string path)
        {
            Console.WriteLine(@"------Read Config-------");

            JsonValue configJson = Json.Parse(File.ReadAllText(path));
            try
            {
                ADnsSetting.ListenIp = IPAddress.Parse(configJson.AsObjectGetString("Listen"));
            }
            catch 
            {
                ADnsSetting.ListenIp = IPAddress.Any;
            }

            try
            {
                ADnsSetting.BlackListEnable = configJson.AsObjectGetBool("BlackList");
            }
            catch
            {
                ADnsSetting.BlackListEnable = false;
            }

            try
            {
                ADnsSetting.ChinaListEnable = configJson.AsObjectGetBool("ChinaList");
            }
            catch
            {
                ADnsSetting.ChinaListEnable = false;
            }

            try
            {
                ADnsSetting.WhiteListEnable = configJson.AsObjectGetBool("RewriteList");
            }
            catch
            {
                ADnsSetting.WhiteListEnable = false;
            }

            try
            {
                ADnsSetting.ProxyEnable = configJson.AsObjectGetBool("ProxyEnable");
            }
            catch
            {
                ADnsSetting.ProxyEnable = false;
            }

            try
            {
                ADnsSetting.IPv6Enable = configJson.AsObjectGetBool("IPv6Enable");
            }
            catch
            {
                ADnsSetting.IPv6Enable = true;
            }

            try
            {
                ADnsSetting.AllowSelfSignedCert = configJson.AsObjectGetBool("AllowSelfSignedCert");
            }
            catch
            {
                ADnsSetting.AllowSelfSignedCert = false;
            }

            try
            {
                ADnsSetting.EDnsCustomize = configJson.AsObjectGetBool("EDnsCustomize");
            }
            catch
            {
                ADnsSetting.EDnsCustomize = false;
            }

            try
            {
                ADnsSetting.DebugLog = configJson.AsObjectGetBool("DebugLog");
            }
            catch
            {
                ADnsSetting.DebugLog = false;
            }

            try
            {
                ADnsSetting.EDnsIp = IPAddress.Parse(configJson.AsObjectGetString("EDnsClientIp"));
            }
            catch
            {
                ADnsSetting.EDnsIp = IPAddress.Any;
            }

            try
            {
                ADnsSetting.HttpsDnsUrl = configJson.AsObjectGetString("HttpsDns");
                if (string.IsNullOrEmpty(ADnsSetting.HttpsDnsUrl))
                {
                    ADnsSetting.HttpsDnsUrl = "https://1.0.0.1/dns-query";
                }
            }
            catch
            {
                ADnsSetting.HttpsDnsUrl = "https://1.0.0.1/dns-query";
            }

            Console.WriteLine(@"Listen        : " + ADnsSetting.ListenIp);
            Console.WriteLine(@"BlackList     : " + ADnsSetting.BlackListEnable);
            Console.WriteLine(@"RewriteList   : " + ADnsSetting.WhiteListEnable);
            Console.WriteLine(@"ChinaList     : " + ADnsSetting.WhiteListEnable);
            Console.WriteLine(@"ProxyEnable   : " + ADnsSetting.ProxyEnable);
            Console.WriteLine(@"IPv6Enable    : " + ADnsSetting.IPv6Enable);
            Console.WriteLine(@"DebugLog      : " + ADnsSetting.DebugLog);
            Console.WriteLine(@"EDnsClient    : " + ADnsSetting.EDnsIp);
            Console.WriteLine(@"HttpsDns      : " + ADnsSetting.HttpsDnsUrl);
            Console.WriteLine(@"EDnsCustomize : " + ADnsSetting.EDnsCustomize);
            if (ADnsSetting.AllowSelfSignedCert)
            {
                Console.WriteLine(@"AllowSelfSignedCert : " + ADnsSetting.AllowSelfSignedCert);
            }

            if (ADnsSetting.ProxyEnable)
            {
                ADnsSetting.WProxy = new WebProxy(configJson.AsObjectGetString("Proxy"));
                Console.WriteLine(@"ProxyServer : " + configJson.AsObjectGetString("Proxy"));
            }
        }

    }
}
