using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.ServiceModel;
using System.Text;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Xml;
using System.Xml.Schema;
using System.Collections;
using System.Net;
using System.Web.Services.Protocols;

namespace Reports.UnitTests
{
    /// <summary>
    /// 
    /// </summary>
    public class ReportHelper
    {
        #region fields
        private Dictionary<string, string> cachedFiles = new Dictionary<string, string>();
        private Dictionary<string, XElement> cachedReports = new Dictionary<string, XElement>();
        private static XElement StripNS(XElement root)
        {
            XElement res = new XElement(
                root.Name.LocalName,
                root.HasElements ?
                    root.Elements().Select(el => StripNS(el)) :
                    (object)root.Value
            );

            res.ReplaceAttributes(
                root.Attributes().Where(attr => (!attr.IsNamespaceDeclaration)));

            return res;
        }
        private static XmlNamespaceManager testNSManager = null;
        private enum ReportServerMode
        {
            Native = 1,
            SharePoint = 2
        }
        #endregion

        #region public methods

        public void Load(string SettingsFilePath, string ResultFilePath)
        {
            try
            {
                XDocument settings = new XDocument();
                if (File.Exists(SettingsFilePath))
                {
                    try
                    {
                        settings = XDocument.Load(SettingsFilePath);

                        XmlSchemaSet schemas = new XmlSchemaSet();
                        schemas.Add("", AppDomain.CurrentDomain.BaseDirectory + "Settings.xsd");

                        string validationErrorMessage = string.Empty;
                        settings.Validate(schemas, (o, ex) =>
                        {
                            validationErrorMessage = ex.Message;
                        });

                        if (!string.IsNullOrEmpty(validationErrorMessage))
                        {
                            LogMessage("File " + SettingsFilePath + " has incompartible schema structure. Error: " + validationErrorMessage);
                            throw new ApplicationException(validationErrorMessage);
                        }
                    }
                    catch (FileLoadException ex)
                    {
                        LogMessage("File " + SettingsFilePath + " was not found or not available. Error: " + ex.Message);
                        throw;
                    }
                }
                else
                {
                    try
                    {
                        settings = XDocument.Parse(SettingsFilePath); //try to load text as XML
                    }
                    catch (XmlException ex)
                    {
                        LogMessage("Settings argument is not valid xml: " + ex.Message);
                        throw;
                    }
                }

                var proc = new XProcessingInstruction("xml-stylesheet", "type=\"text/xsl\" href=\"Settings.xsl\"");
                settings.Root.AddBeforeSelf(proc);
                XAttribute runAttribute = new XAttribute("RunAt", DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss"));
                settings.Root.Add(runAttribute);
#if DEBUG
                Debug.Flush();
#endif
                foreach (XElement server in settings.Descendants("ReportServer"))
                {
                    string reportServerPath = server.Attribute("Path").Value;
                    string userName = string.Empty;
                    string userPassword = string.Empty;
                    if (server.Attribute("UserName") != null && server.Attribute("UserPassword") != null)
                    {
                        userName = server.Attribute("UserName").Value;
                        userPassword = server.Attribute("UserPassword").Value;
                    }
                    HttpClientCredentialType reportHttpClientCredentialType = HttpClientCredentialType.Windows;
                    if (server.Attribute("HttpClientCredentialType") != null
                        && server.Attribute("HttpClientCredentialType").Value != null
                        && server.Attribute("HttpClientCredentialType").Value.ToLower() == "ntlm")
                    {
                        reportHttpClientCredentialType = HttpClientCredentialType.Ntlm;
                    }

                    //Console.WriteLine(string.Format("ReportServer: {0}; userName:{1}; userPassword:{2}", reportServerPath, userName, userPassword));
                    string ParameterLanguage = (server.Attribute("ParameterLanguage") != null ? server.Attribute("ParameterLanguage").Value : "en-us").ToLower();
                    ReportServerMode reportServerMode = (server.Attribute("Mode").Value == "Native" ? ReportServerMode.Native : ReportServerMode.SharePoint);

                    foreach (XElement folder in server.Descendants("Folder"))
                    {
                        string reportFolder = folder.Attribute("Name").Value;
                        //Console.WriteLine(string.Format("reportFolder: {0};", reportFolder));
                        foreach (XElement report in folder.Descendants("Report"))
                        {
                            string reportName = report.Attribute("Name").Value;
                            List<KeyValuePair<string, string>> reportParams = new List<KeyValuePair<string, string>>();
                            if (report.Element("Params") != null)
                            {
                                foreach (XElement param in report.Element("Params").Descendants("Param"))
                                {
                                    if (!string.IsNullOrEmpty(param.Attribute("Value").Value))
                                    {
                                        reportParams.Add(new KeyValuePair<string, string>(param.Attribute("Name").Value, param.Attribute("Value").Value));
                                    }
                                }
                            }
                            #region Render Report
                            if (report.Attribute("RenderFormat") != null) //&& report.Attribute("RenderPath") != null
                            {
                                //XML|CSV|IMAGE|PDF|EXCEL|WORD|HTML 4.0|MHTML|NULL http://msdn.microsoft.com/en-us/library/ms154606.aspx
                                string renderFormat = report.Attribute("RenderFormat").Value;
                                string renderPath = (report.Attribute("RenderPath") != null ? report.Attribute("RenderPath").Value : string.Empty);

                                Render(reportServerPath, userName, userPassword, reportServerMode, reportHttpClientCredentialType, reportFolder, reportName, reportParams, ParameterLanguage, renderFormat, renderPath);
                            }
                            #endregion

                            #region linked reports
                            if (report.Element("LinkedReports") != null)
                            {
                                foreach (XElement linkedReport in report.Element("LinkedReports").Descendants("LinkedReport"))
                                {
                                    string linkedReportPath = linkedReport.Attribute("Path").Value;
                                    ReportingService2010.Property prop = new ReportingService2010.Property();
                                    prop.Name = "Description";
                                    string linkedReportDescription = string.Empty;
                                    if (linkedReport.Attribute("Description") != null)
                                    {
                                        linkedReportDescription = linkedReport.Attribute("Description").Value;
                                    }
                                    prop.Value = linkedReportDescription;

                                    ReportingService2010.Property[] linkedReportProperties = new ReportingService2010.Property[1];
                                    linkedReportProperties[0] = prop;

                                    CreateLinkedReport(reportServerPath, userName, userPassword, reportServerMode, reportHttpClientCredentialType, reportFolder, reportName
                                                , linkedReportPath
                                                , linkedReportProperties
                                                , linkedReport.Element("Params")
                                        //, ParameterLanguage
                                    );
                                }
                            }
                            #endregion

                            #region test cases
                            if (report.Element("TestCases") != null)
                            {
                                XElement xDocument = null;
                                Render(reportServerPath, userName, userPassword, reportServerMode, reportHttpClientCredentialType, reportFolder, reportName, reportParams, ParameterLanguage, out xDocument);
                                foreach (XElement test in report.Element("TestCases").Descendants("TestCase"))
                                {
                                    if (test.Attribute("Assert").Value == "IsNotNull")
                                    {
                                        string value = GetValue(xDocument, reportName, test.Attribute("Path").Value);
                                        XAttribute attribute = new XAttribute("Passed", ((!string.IsNullOrEmpty(value)).ToString()));
                                        test.Add(attribute);
                                    }
                                    else if (test.Attribute("Assert").Value == "AreEqual")
                                    {
                                        string parentValue = GetValue(xDocument, reportName, test.Attribute("Path").Value);
                                        string childValue = string.Empty;
                                        XAttribute attributeValue = test.Attribute("Value");
                                        if (attributeValue != null)
                                        {
                                            childValue = attributeValue.Value;
                                        }
                                        else
                                        {
                                            XElement childReport = test.Element("DrillDownReport");
                                            if (childReport != null)
                                            {
                                                List<KeyValuePair<string, string>> childReportParams = new List<KeyValuePair<string, string>>();
                                                foreach (XElement param in childReport.Element("Params").Descendants("Param"))
                                                {
                                                    childReportParams.Add(new KeyValuePair<string, string>(param.Attribute("Name").Value, param.Attribute("Value").Value));
                                                }
                                                XElement xChildDocument = null;
                                                Render(reportServerPath, userName, userPassword, reportServerMode, reportHttpClientCredentialType, reportFolder, childReport.Attribute("Name").Value, childReportParams, ParameterLanguage, out xChildDocument);
                                                childValue = GetValue(xChildDocument, childReport.Attribute("Name").Value, childReport.Attribute("Path").Value);
                                            }
                                        }
                                        XAttribute attribute = new XAttribute("Passed", (parentValue == childValue).ToString());
                                        test.Add(attribute);
                                    }
                                }
                                if (string.IsNullOrEmpty(ResultFilePath))
                                {
                                    ResultFilePath = AppDomain.CurrentDomain.BaseDirectory + "TestSuite " + DateTime.Now.ToString("yyyy-MM-dd hh-mm-ss") + ".xml";
                                }
                            }
                            #endregion
                        }
                    }
                }
                if (!string.IsNullOrEmpty(ResultFilePath))
                {
                    try
                    {
                        settings.Save(ResultFilePath);
                    }
                    catch (IOException ex)
                    {
                        LogMessage("File " + ResultFilePath + " can not be saved. Error: " + ex.Message);
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage(ex.Message + "  \n" + ex.StackTrace);
                throw;
            }
        }

        #endregion

        #region private methods

        private void Render(string ReportServerPath, string UserName, string UserPassword, ReportServerMode ReportMode, HttpClientCredentialType ReportHttpClientCredentialType, string ReportFolder, string ReportName, List<KeyValuePair<string, string>> ReportParameters, string ParameterLanguage, out XElement xDocument)
        {
            xDocument = null;
            ReportExecution2005.ParameterValue[] parameters = new ReportExecution2005.ParameterValue[ReportParameters.Count()];
            int index = 0;
            string paramString = string.Empty;
            foreach (var item in ReportParameters)
            {
                parameters[index] = new ReportExecution2005.ParameterValue();
                parameters[index].Name = item.Key;
                parameters[index].Value = item.Value;
                index++;
                paramString = paramString + item.Key + "=" + item.Value + "&";
            }

            string cachedKey = ReportServerPath.TrimEnd('/') + @"/" + ReportFolder.TrimStart('/').TrimEnd('/') + @"/" + ReportName + @"?" + paramString;
            if (cachedReports.ContainsKey(cachedKey)) //load report from cache
            {
                xDocument = cachedReports[cachedKey];
                return;
            }

            byte[] bytes;
            GetReportData(ReportServerPath, UserName, UserPassword, ReportMode, ReportHttpClientCredentialType, ReportFolder, ReportName, parameters, ParameterLanguage, "XML", out bytes);
            if (bytes != null)
            {
                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    XNamespace test = XNamespace.Get(ReportName);
                    XNamespace xsi = XNamespace.Get(@"http://www.w3.org/2001/XMLSchema-instance");
                    XmlReader reader = XmlReader.Create(ms);
                    xDocument = StripNS(XElement.Load(reader, LoadOptions.None));
                    xDocument.Attributes().Remove();
                    xDocument = new XElement("Report"
                        , new XAttribute(XNamespace.Xmlns + "test", ReportName) //blank.NamespaceName
                        , new XAttribute(XNamespace.Xmlns + "xsi", xsi)
                        , xDocument.Nodes());
                }
                cachedReports[cachedKey] = xDocument;
            }
        }

        private void Render(string ReportServerPath, string UserName, string UserPassword, ReportServerMode ReportMode, HttpClientCredentialType ReportHttpClientCredentialType, string ReportFolder, string ReportName, List<KeyValuePair<string, string>> ReportParameters, string ParameterLanguage, string RenderFormat, string RenderPath)
        {
            ReportExecution2005.ParameterValue[] parameters = new ReportExecution2005.ParameterValue[ReportParameters.Count()];
            int index = 0;
            string paramString = string.Empty;
            foreach (var item in ReportParameters)
            {
                parameters[index] = new ReportExecution2005.ParameterValue();
                parameters[index].Name = item.Key;
                parameters[index].Value = item.Value;
                index++;
                paramString = paramString + item.Key + "=" + item.Value + "&";
            }

            string cachedKey = ReportServerPath + @"/" + ReportFolder + @"/" + ReportName + @"?" + paramString;
            if (cachedFiles.ContainsKey(cachedKey)) //load report from cache
            {
                RenderPath = cachedFiles[cachedKey];
                if (!string.IsNullOrEmpty(RenderPath) && File.Exists(RenderPath))
                {
                    return;
                }
            }
            byte[] bytes;
            GetReportData(ReportServerPath, UserName, UserPassword, ReportMode, ReportHttpClientCredentialType, ReportFolder, ReportName, parameters, ParameterLanguage, RenderFormat, out bytes);
            if (bytes != null && !string.IsNullOrEmpty(RenderPath))
            {
                try
                {
                    string folderName = RenderPath.Substring(0, RenderPath.LastIndexOf('\\') + 1);
                    string fileName = CleanFileName(RenderPath.Substring(RenderPath.LastIndexOf('\\') + 1));
                    using (FileStream fs = File.Create(folderName + fileName))
                    {
                        fs.Write(bytes, 0, bytes.Length);
                        fs.Close();
                    }
                    cachedFiles[cachedKey] = RenderPath;
                }
                catch (IOException ex)
                {
                    LogMessage("Not able to create file " + RenderPath + ". Error: " + ex.Message);
                    throw;
                }
            }
        }

        private static string CleanFileName(string fileName)
        {
            return Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c.ToString(), string.Empty));
        }

        private void GetReportData(string ReportServerPath, string UserName, string UserPassword, ReportServerMode ReportMode, HttpClientCredentialType ReportHttpClientCredentialType, string ReportFolder, string ReportName, ReportExecution2005.ParameterValue[] Parameters, string ParameterLanguage, string RenderFormat, out byte[] bytes)
        {
            bytes = null;
            string serviceUrl;
            string execUrl;
            BasicHttpBinding basicHttpBinding;
            ConfigureReportServerBinding(ReportServerPath, ReportMode, ReportHttpClientCredentialType, out serviceUrl, out execUrl, out basicHttpBinding);

            using (ReportingService2010.ReportingService2010SoapClient rsService = new ReportingService2010.ReportingService2010SoapClient(basicHttpBinding, new EndpointAddress(serviceUrl)))
            using (ReportExecution2005.ReportExecutionServiceSoapClient rsExec = new ReportExecution2005.ReportExecutionServiceSoapClient(basicHttpBinding, new EndpointAddress(execUrl)))
            {
                ReportingService2010.TrustedUserHeader trusteduserHeader;
                ReportExecution2005.TrustedUserHeader userHeader;
                GetHeaders(UserName, UserPassword, rsService, rsExec, out trusteduserHeader, out userHeader);

                ReportingService2010.SearchCondition condition = new ReportingService2010.SearchCondition();
                condition.Condition = ReportingService2010.ConditionEnum.Equals;
                condition.ConditionSpecified = true;
                condition.Name = "Name";
                ReportingService2010.SearchCondition[] conditions = new ReportingService2010.SearchCondition[1];
                conditions[0] = condition;
                ReportingService2010.CatalogItem[] foundItems = null;
                condition.Values = new string[] { ReportName };
                rsService.FindItems(trusteduserHeader
                    , "/" + ReportFolder.TrimStart('/').TrimEnd('/')
                    , ReportingService2010.BooleanOperatorEnum.And
                    , new ReportingService2010.Property[0]
                    , conditions
                    , out foundItems);
                if (foundItems != null && foundItems.Count() > 0)
                {
                    foreach (var item in foundItems)
                    {
                        if (item.Path == "/" + ReportFolder.TrimStart('/').TrimEnd('/') + "/" + ReportName)
                        {
                            ///Exporting to XML is not supported in SQL Server Express
                            ///http://msdn.microsoft.com/en-us/library/cc645993.aspx
                            string format = RenderFormat.ToUpper();
                            string historyID = null;
                            string devInfo = @"<DeviceInfo><Toolbar>False</Toolbar></DeviceInfo>";
                            string encoding;
                            string mimeType;
                            string extension;
                            string[] streamIDs = null;
                            ReportExecution2005.Warning[] warnings = null;
                            ReportExecution2005.ServerInfoHeader execServerHeader = new ReportExecution2005.ServerInfoHeader();
                            ReportExecution2005.ExecutionInfo execInfo = new ReportExecution2005.ExecutionInfo();
                            rsExec.LoadReport(userHeader, item.Path, historyID, out execServerHeader, out execInfo);
                            ReportExecution2005.ExecutionHeader execHeader = new ReportExecution2005.ExecutionHeader();
                            execHeader.ExecutionID = execInfo.ExecutionID;

                            rsExec.SetExecutionParameters(execHeader, userHeader, Parameters, ParameterLanguage, out execInfo);
                            execServerHeader = rsExec.Render(execHeader, userHeader, format, devInfo, out bytes, out extension, out mimeType, out encoding, out warnings, out streamIDs);

                            break;
                        }
                    }
                }
            }
        }

        private static void GetHeaders(string UserName, string UserPassword, ReportingService2010.ReportingService2010SoapClient rsService, ReportExecution2005.ReportExecutionServiceSoapClient rsExec, out ReportingService2010.TrustedUserHeader trusteduserHeader, out ReportExecution2005.TrustedUserHeader userHeader)
        {
            rsService.ClientCredentials.SupportInteractive = false;
            rsService.ClientCredentials.Windows.ClientCredential = System.Net.CredentialCache.DefaultNetworkCredentials;
            rsService.ClientCredentials.Windows.AllowedImpersonationLevel = System.Security.Principal.TokenImpersonationLevel.Impersonation;
            System.Net.NetworkCredential clientCredentials = new System.Net.NetworkCredential(UserName, UserPassword);
            if (!string.IsNullOrEmpty(UserName) && !string.IsNullOrEmpty(UserPassword) && UserName != "@UserName" && UserPassword != "@UserPassword")
            {
                rsService.ClientCredentials.Windows.ClientCredential = clientCredentials;
                rsService.ClientCredentials.UserName.UserName = UserName;
                rsService.ClientCredentials.UserName.UserName = UserPassword;
            }
            trusteduserHeader = new ReportingService2010.TrustedUserHeader();
            trusteduserHeader.UserName = clientCredentials.UserName;
            userHeader = new ReportExecution2005.TrustedUserHeader();
            userHeader.UserName = clientCredentials.UserName;
            if (rsExec != null)
            {
                rsExec.ClientCredentials.Windows.AllowedImpersonationLevel = TokenImpersonationLevel.Impersonation;
                //rsExec.ClientCredentials.Windows.ClientCredential = credentials.GetCredential(new Uri(baseUrl), "NTLM");
            }

        }

        private static void ConfigureReportServerBinding(string ReportServerPath, ReportServerMode ReportMode, HttpClientCredentialType ReportHttpClientCredentialType, out string serviceUrl, out string execUrl, out BasicHttpBinding basicHttpBinding)
        {
            serviceUrl = string.Empty;
            execUrl = string.Empty;
            if (ReportMode == ReportServerMode.Native) // for example http://licalhost/ReportServer/
            {
                serviceUrl = ReportServerPath.TrimEnd('/') + @"/reportservice2010.asmx";
                execUrl = ReportServerPath.TrimEnd('/') + @"/ReportExecution2005.asmx";
            }
            else if (ReportMode == ReportServerMode.SharePoint) // for example http://mysharepointserver.local/_vti_bin/ReportServer/
            {
                serviceUrl = ReportServerPath.TrimEnd('/') + @"/_vti_bin/ReportServer/reportservice2010.asmx";
                execUrl = ReportServerPath.TrimEnd('/') + @"/_vti_bin/ReportServer/ReportExecution2005.asmx";
            }

            basicHttpBinding = new BasicHttpBinding(BasicHttpSecurityMode.TransportCredentialOnly);
            basicHttpBinding.TransferMode = TransferMode.Buffered;
            basicHttpBinding.Security.Mode = BasicHttpSecurityMode.TransportCredentialOnly;
            basicHttpBinding.Security.Transport.ProxyCredentialType = HttpProxyCredentialType.None;
            basicHttpBinding.Security.Transport.ClientCredentialType = ReportHttpClientCredentialType; //Windows|Ntlm
            basicHttpBinding.Security.Message.ClientCredentialType = BasicHttpMessageCredentialType.UserName;
            basicHttpBinding.MaxReceivedMessageSize = 2147483646L;
            ////basicHttpBinding.AllowCookies = true;
            basicHttpBinding.SendTimeout = TimeSpan.MaxValue;
            //basicHttpBinding.Security.Transport.ClientCredentialType = HttpClientCredentialType.Windows;
        }

        private string GetValue(XElement xRoot, string ReportName, string XPath)
        {
            string value = string.Empty;
            if (xRoot != null)
            {
                XPathNavigator navigator = xRoot.CreateNavigator();
                if (testNSManager == null)
                {
                    testNSManager = new XmlNamespaceManager(navigator.NameTable);
                    testNSManager.AddNamespace("test", ReportName);
                }

                IEnumerable xItems = (IEnumerable)xRoot.XPathEvaluate(XPath, testNSManager);
                if (XPath.Contains("/@"))
                {
                    IEnumerable<XAttribute> attList = xItems.Cast<XAttribute>();
                    XAttribute att = attList.FirstOrDefault();
                    if (att != null)
                        value = att.Value;
                }
                else
                {
                    IEnumerable<XElement> elemList = xItems.Cast<XElement>();
                    XElement elem = elemList.FirstOrDefault();
                    if (elem != null)
                        value = elem.Value;
                }
            }
            return value;
        }

        private void CreateLinkedReport(string ReportServerPath, string UserName, string UserPassword, ReportServerMode ReportMode
            , HttpClientCredentialType ReportHttpClientCredentialType
            , string ReportFolder, string ReportName
            , string LinkedReportPath
            , ReportingService2010.Property[] LinkedReportProperties
            , XElement xLinkedReportParameters
            //, string ParameterLanguage
            )
        {
            string serviceUrl;
            string execUrl;
            BasicHttpBinding basicHttpBinding;
            ConfigureReportServerBinding(ReportServerPath, ReportMode, ReportHttpClientCredentialType, out serviceUrl, out execUrl, out basicHttpBinding);

            using (ReportingService2010.ReportingService2010SoapClient rsService = new ReportingService2010.ReportingService2010SoapClient(basicHttpBinding, new EndpointAddress(serviceUrl)))
            {
                ReportingService2010.TrustedUserHeader trusteduserHeader;
                ReportExecution2005.TrustedUserHeader userHeader;
                GetHeaders(UserName, UserPassword, rsService, null, out trusteduserHeader, out userHeader);

                ReportingService2010.SearchCondition condition = new ReportingService2010.SearchCondition();
                condition.Condition = ReportingService2010.ConditionEnum.Equals;
                condition.ConditionSpecified = true;
                condition.Name = "Name";
                ReportingService2010.SearchCondition[] conditions = new ReportingService2010.SearchCondition[1];
                conditions[0] = condition;
                ReportingService2010.CatalogItem[] foundItems = null;

                condition.Values = new string[] { ReportName.TrimStart('/').TrimEnd('/') };
                rsService.FindItems(trusteduserHeader
                    , "/" + ReportFolder.TrimStart('/').TrimEnd('/')
                    , ReportingService2010.BooleanOperatorEnum.And
                    , new ReportingService2010.Property[0]
                    , conditions
                    , out foundItems);

                if (foundItems != null && foundItems.Count() > 0)
                {
                    foreach (var item in foundItems)
                    {
                        if (item.Path == "/" + ReportFolder.TrimStart('/').TrimEnd('/') + "/" + ReportName)
                        {
                            string parentReportPath = item.Path;
                            try
                            {
                                string linkedReportName = LinkedReportPath.Substring(LinkedReportPath.LastIndexOf('/') + 1).TrimStart('/').TrimEnd('/');
                                string linkedReportFolderPath = LinkedReportPath.Substring(0, LinkedReportPath.LastIndexOf('/')).TrimEnd('/');
                                CheckLinkedFolderExists(rsService, trusteduserHeader, condition, conditions, linkedReportFolderPath);
                                ReportingService2010.CatalogItem[] foundLinkedItems = null;
                                condition.Values = new string[] { linkedReportName };
                                rsService.FindItems(trusteduserHeader
                                    , linkedReportFolderPath
                                    , ReportingService2010.BooleanOperatorEnum.And
                                    , new ReportingService2010.Property[0]
                                    , conditions
                                    , out foundLinkedItems);

                                if (foundLinkedItems == null || (foundLinkedItems != null && foundLinkedItems.Count() == 0))
                                {
                                    rsService.CreateLinkedItem(trusteduserHeader
                                        , linkedReportName
                                        , linkedReportFolderPath
                                        , parentReportPath
                                        , LinkedReportProperties);
                                }
                                if (xLinkedReportParameters != null)
                                {
                                    //// List of properties to copy from parent to linked reports. ???
                                    //var requestedProperties = new ReportingService2010.Property[] {
                                    //    new ReportingService2010.Property { Name = "PageHeight" },
                                    //    new ReportingService2010.Property { Name = "PageWidth" },
                                    //    new ReportingService2010.Property { Name = "TopMargin" },
                                    //    new ReportingService2010.Property { Name = "BottomMargin" },
                                    //    new ReportingService2010.Property { Name = "LeftMargin" },
                                    //    new ReportingService2010.Property { Name = "RightMargin" }
                                    //};

                                    IEnumerable<XElement> linkedReportParams = xLinkedReportParameters.Descendants("Param");
                                    if (linkedReportParams.Count() > 0)
                                    {
                                        List<ReportingService2010.ItemParameter> ItemParamsList = new List<ReportingService2010.ItemParameter>();
                                        foreach (var linkedReportParam in linkedReportParams)
                                        {
                                            string defaultValues = linkedReportParam.Attribute("DefaultValues").Value;
                                            if (!string.IsNullOrEmpty(defaultValues))
                                            {
                                                ReportingService2010.ItemParameter[] parentReportParams = null;
                                                rsService.GetItemParameters(trusteduserHeader
                                                    , parentReportPath
                                                    , null
                                                    , false, null, null, out parentReportParams);

                                                bool paramExists = false;
                                                ReportingService2010.ItemParameter ip = new ReportingService2010.ItemParameter();
                                                ip.Name = linkedReportParam.Attribute("Name").Value;
                                                foreach (var parentReportParam in parentReportParams)
                                                {
                                                    if (parentReportParam.Name == ip.Name)
                                                    {
                                                        ip.AllowBlank = parentReportParam.AllowBlank;
                                                        ip.MultiValue = parentReportParam.MultiValue;
                                                        ip.Nullable = parentReportParam.Nullable;
                                                        ip.Prompt = parentReportParam.Prompt;
                                                        ip.PromptUser = parentReportParam.PromptUser;
                                                        ip.PromptUserSpecified = parentReportParam.PromptUserSpecified;
                                                        paramExists = true;
                                                        break;
                                                    }

                                                }
                                                if (paramExists)
                                                {
                                                    ip.DefaultValues = defaultValues.Split(',');
                                                    if (linkedReportParam.Attribute("Hide") != null
                                                        && linkedReportParam.Attribute("Hide").Value.ToLower() == "true")
                                                    { //hide the paramerter using combination of parameters. There is no Hide property which reflects UI Hide checkbox
                                                        ip.PromptUser = false;
                                                        ip.PromptUserSpecified = false;
                                                        ip.Prompt = null;
                                                    }
                                                    ItemParamsList.Add(ip);
                                                }
                                            }
                                        }
                                        rsService.SetItemParameters(trusteduserHeader, LinkedReportPath, ItemParamsList.ToArray());
                                    }
                                }
                            }
                            catch (SoapException ex)
                            {
                                LogMessage("Not able to create linked report " + LinkedReportPath + " Error: " + ex.Message);
                                throw;
                            }

                            break;
                        }
                    }
                }
            }
        }

        private void CheckLinkedFolderExists(ReportingService2010.ReportingService2010SoapClient rsService
            , ReportingService2010.TrustedUserHeader trusteduserHeader
            , ReportingService2010.SearchCondition condition
            , ReportingService2010.SearchCondition[] conditions
            , string linkedReportFolderPath
            )
        {
            string parentFolderPath = @"/";
            string[] folders = linkedReportFolderPath.Split('/');
            for (int i = 0; i < folders.Length; i++)
            {
                if (!string.IsNullOrEmpty(folders[i]))
                {
                    ReportingService2010.CatalogItem[] foundItems = null;
                    condition.Values = new string[] { folders[i] };
                    rsService.FindItems(trusteduserHeader
                        , parentFolderPath
                        , ReportingService2010.BooleanOperatorEnum.And
                        , new ReportingService2010.Property[0]
                        , conditions
                        , out foundItems);

                    if (foundItems == null || (foundItems != null && foundItems.Count() == 0))
                    {
                        ReportingService2010.CatalogItem linkedFolder = new ReportingService2010.CatalogItem();
                        rsService.CreateFolder(trusteduserHeader
                            , folders[i]
                            , parentFolderPath
                            , new ReportingService2010.Property[0]
                            , out linkedFolder
                            );
                    }
                    parentFolderPath = parentFolderPath.TrimEnd('/') + @"/" + folders[i];
                }
            }
        }

        private static void LogMessage(string Message)
        {
            string filePath = AppDomain.CurrentDomain.BaseDirectory + "Error.log";
            if (!File.Exists(filePath))
            {
                using (StreamWriter fs = File.CreateText(filePath))
                {
                    fs.Close();
                }
            }
            using (StreamWriter fs = File.AppendText(filePath))
            {
                fs.WriteLine("-----------");
                fs.WriteLine(string.Format("Date:{0}; Message:{1}\n"
                    , DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss")
                    , Message));
                fs.Close();
            }
        }

        #endregion
    }
}