﻿namespace AzureSpeed.Web.App.ApiControllers
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Web.Http;
    using System.Xml;
    using Common;
    using LukeSkywalker.IPNetwork;
    using Microsoft.AspNetCore.Hosting.Internal;
    using Newtonsoft.Json;
    using NLog.Internal;
    using WebUI.Common;
    using WebUI.Models;
    using WebUI.ViewModels;

    [RoutePrefix("api")]
    public class AzureApiController : ApiControllerBase
    {
        [Microsoft.AspNetCore.Mvc.HttpGet]
        [Microsoft.AspNetCore.Mvc.Route("ip")]
        public IHttpActionResult GetIp()
        {
            var ip = ((HttpContextBase)Request.Properties["MS_HttpContext"]).Request.UserHostAddress;
            return Ok(ip);
        }

        [Microsoft.AspNetCore.Mvc.HttpGet]
        [Microsoft.AspNetCore.Mvc.Route("region")]
        public IHttpActionResult GetRegionName(string ipOrUrl)
        {
            return Ok(GetRegionNameByIpOrUrl(ipOrUrl));
        }

        [Microsoft.AspNetCore.Mvc.HttpGet]
        [Microsoft.AspNetCore.Mvc.Route("iprange")]
        public IHttpActionResult GetIpRange()
        {
            return Ok(GetSubnetList());
        }

        [Microsoft.AspNetCore.Mvc.HttpGet]
        [Microsoft.AspNetCore.Mvc.Route("sas")]
        public IHttpActionResult GetSasLink(string region, string blobName, string operation)
        {
            string url = string.Empty;
            if (!string.IsNullOrEmpty(region))
            {
                var account = AzureSpeedData.Accounts.FirstOrDefault(v => v.Region == region);
                if (account != null)
                {
                    var storageContext = new StorageContext(account);
                    url = storageContext.GetSasUrl(blobName, operation);
                }
            }

            return Ok(url);
        }

        [Microsoft.AspNetCore.Mvc.HttpGet]
        [Microsoft.AspNetCore.Mvc.Route("cleanup")]
        public IHttpActionResult CleanUpBlobs()
        {
            foreach (var account in AzureSpeedData.Accounts)
            {
                var storageAccount = new StorageContext(account);
                storageAccount.CleanUpBlobs();
            }

            return Ok();
        }

        public string GetRegionNameByIpOrUrl(string ipOrUrl, string ipFilePath = null)
        {
            if (string.IsNullOrEmpty(ipOrUrl))
            {
                return "Must specify a valid ipAddress or url";
            }

            if (string.IsNullOrEmpty(ipFilePath))
            {
                ipFilePath = HostingEnvironment.MapPath("~/App_Data/");
            }

            if (!(ipOrUrl.StartsWith("http://") || ipOrUrl.StartsWith("https://")))
            {
                ipOrUrl = "http://" + ipOrUrl;
            }

            Uri tmp = new Uri(ipOrUrl);
            ipOrUrl = tmp.Host;

            var sw = new Stopwatch();
            sw.Start();

            var ips = Dns.GetHostAddresses(ipOrUrl);
            var ipAddr = ips[0];
            var subnets = SubnetBuilder.GetSubnetDictionary(ipFilePath);
            foreach (IPNetwork net in subnets.Keys)
            {
                if (IPNetwork.Contains(net, ipAddr))
                {
                    var regionAlias = subnets[net];
                    sw.Stop();
                    string region = AzureSpeedData.RegionNames[regionAlias];
                    Logger.Info($"IpOrUrl = {ipOrUrl}, region = {region}, time = {sw.ElapsedMilliseconds}");
                    return region;
                }
            }

            return "Region not found";
        }

        public List<IpRangeViewModel> GetSubnetList(string ipFilePath = null)
        {
            if (string.IsNullOrEmpty(ipFilePath))
            {
                ipFilePath = HostingEnvironment.MapPath("~/App_Data/");
            }

            var result = new List<IpRangeViewModel>();

            // Load Azure ip range data
            string ipFileList = ConfigurationManager.AppSettings["AzureIpRangeFileList"];
            foreach (string filePath in ipFileList.Split(';'))
            {
                var xmlDoc = new XmlDocument();
                xmlDoc.Load(ipFilePath + @"\IpRangeFiles\Azure\" + filePath);
                var root = xmlDoc.DocumentElement;
                foreach (XmlElement ele in root)
                {
                    string region = ele.GetAttribute("Name");
                    var ipRange = new IpRangeViewModel { Cloud = "Azure", Region = region, Subnet = new List<string>() };
                    foreach (XmlElement xe in ele)
                    {
                        string subnet = xe.GetAttribute("Subnet");
                        ipRange.Subnet.Add(subnet);
                        var network = IPNetwork.Parse(subnet);
                        ipRange.TotalIpCount += network.Total;
                    }

                    result.Add(ipRange);
                }
            }

            // Load AWS ip range data
            string awsIpFile = ConfigurationManager.AppSettings["AwsIpRangeFile"];
            string json = File.ReadAllText(ipFilePath + @"\IpRangeFiles\AWS\" + awsIpFile);
            var awsIpRangeData = JsonConvert.DeserializeObject<AwsIpRangeData>(json);
            foreach (var prefix in awsIpRangeData.Prefixes)
            {
                string region = prefix.Region;
                string subnet = prefix.IpPrefix;
                if (result.Any(v => v.Region == region))
                {
                    var ipRange = result.First(v => v.Region == region);
                    ipRange.Subnet.Add(subnet);
                    var network = IPNetwork.Parse(subnet);
                    ipRange.TotalIpCount += network.Total;
                }
                else
                {
                    var ipRange = new IpRangeViewModel { Cloud = "AWS", Region = region, Subnet = new List<string>() };
                    ipRange.Subnet.Add(subnet);
                    var network = IPNetwork.Parse(subnet);
                    ipRange.TotalIpCount += network.Total;
                    result.Add(ipRange);
                }
            }

            // Load AliCloud ip range data
            string aliCloudIpFile = ConfigurationManager.AppSettings["AliCloudIpRangeFile"];
            string[] lines = File.ReadAllLines(ipFilePath + @"\IpRangeFiles\AliCloud\" + aliCloudIpFile);
            var aliIpRange = new IpRangeViewModel { Cloud = "AliCloud", Region = "AliCloud", Subnet = new List<string>() };
            foreach (var line in lines)
            {
                string subnet = line;
                aliIpRange.Subnet.Add(subnet);
                var network = IPNetwork.Parse(subnet);
                aliIpRange.TotalIpCount += network.Total;
            }

            result.Add(aliIpRange);

            return result;
        }
    }
}