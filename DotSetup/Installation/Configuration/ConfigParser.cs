﻿// Copyright (c) dotSetup. All Rights Reserved.
// Licensed under the GPL License, version 3.0.
// https://dotsetup.io/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;

namespace DotSetup
{
    public struct ProductSettings
    {
        public string Name, Publisher, Filename, RunPath, ExtractPath, RunParams, LayoutName, Behavior, Skin, Class, DownloadMethod, SecondaryDownloadMethod;

        public struct DownloadURL
        {
            public string Arch, URL;
        }
        public List<DownloadURL> DownloadURLs;

        public struct ProductEvent
        {
            public string Type, Triger;
        }
        public List<ProductEvent> ProductEvents;

        public struct RequirementKey
        {
            public string KeyType;
            public string KeyValue;
        }

        public struct ProductRequirement
        {
            public List<RequirementKey> Keys;
            public string Type, Value, LogicalOperator, ValueOperator, Delta;
        }
        public struct ProductRequirements
        {
            public List<ProductRequirement> RequirementList;
            public List<ProductRequirements> RequirementsList;
            public string LogicalOperator, UnfulfilledRequirementType, UnfulfilledRequirementDelta;
        }
        public ProductRequirements PreInstall;
        public ProductRequirements PostInstall;
        public ControlsLayout ControlsLayouts;
        public bool IsOptional, IsExtractable, RunWithBits;
        public int MsiTimeoutMS;
        public Dictionary<string, string> AnalyticsParams;
    }
    public struct FormDesign
    {
        public int Height, Width, ClientHeight, ClientWidth, BottomPanelHeight;
        public string FormName;
        public Color BackgroundColor;
        public Dictionary<string, string> DefaultControlDesign;
    }

    public struct PageDesign
    {
        public string PageName;
        public ControlsLayout ControlsLayouts;
        public int Index;
    }

    public class ConfigParser
    {
        private XmlDocument xmlDoc;
        private FormDesign formDesign;
        private List<PageDesign> pagesDesign;
        private List<ProductSettings> productsSettings;

        public string LocaleCode { get; private set; }
        public string workDir;

        private static ConfigParser _configParser;
        private static string userSelectedLocale;

        public static ConfigParser GetConfig()
        {
            return _configParser;
        }

        public ConfigParser()
        {
            _configParser = this;
            Stream mainXmlStream = ResourcesUtils.GetEmbeddedResourceStream(null, "main.xml");
            if (mainXmlStream == null)
                mainXmlStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<Main><Products></Products></Main>"));

            Stream configXmlStream = ResourcesUtils.GetEmbeddedResourceStream(null, "config.xml");

            if (configXmlStream == null)
            {
#if DEBUG
                Logger.GetLogger().Error("No resource called config.xml");
#endif
            }

            ReadXmlFiles(new Stream[] { mainXmlStream, configXmlStream });

            ReadResourcesToXml();
            ReadCmdToXml();
            ReadConfigSettings();

            SetFormDesign();
            SetPagesDesign();
        }

        public void ResolveSettings()
        {
            SetFormDesign();
            SetPagesDesign();
            ReadProductsSettings();

            if (CmdReader.CmdParams.ContainsKey("saveconfig"))
                xmlDoc.Save(CmdReader.CmdParams["saveconfig"]);
        }

        public ConfigParser(XmlDocument xmlDoc)
        {
            _configParser = this;
            this.xmlDoc = xmlDoc;
        }

        private void ReadXmlFiles(Stream[] xmlFileStreams)
        {
            foreach (Stream xmlStream in xmlFileStreams)
            {
                if (xmlStream != null)
                {
                    if (xmlDoc == null)
                    {
                        xmlDoc = new XmlDocument();
                        xmlDoc.Load(xmlStream);
                    }
                    else
                    {
                        XmlDocument tempDoc = new XmlDocument();
                        tempDoc.Load(xmlStream);
                        XmlParser.SetNode(xmlDoc, tempDoc.DocumentElement);
                    }
                }
            }
        }

        public void SetConfigValue(string entryKey, string entryValue)
        {
            if (ConfigConsts.SensitiveConfigKeys.Contains(entryKey.ToUpper()))
            {
#if DEBUG
                Logger.GetLogger().Error("Cannot override the key: " + entryKey + ", this key cannot be injected");
#endif
            }
            else
            {
                string xpath = entryKey;
                if (!xpath.StartsWith("//"))
                    xpath = "//Config/" + xpath;
                SetStringValue(xpath, ParseConfigValue(entryKey, entryValue));
            }
        }

        private void ReadResourcesToXml()
        {
            Dictionary<string, string> resourceSet = ResourcesUtils.GetPropertiesResources(null);
#if DEBUG
            Logger.GetLogger().Info("Adding dynamic config from resources.");
#endif
            foreach (KeyValuePair<string, string> entry in resourceSet)
            {
                SetConfigValue(entry.Key, entry.Value);
            }
        }

        private string ParseConfigValue(string entryKey, string entryValue)
        {
            try
            {
                entryValue = System.Web.HttpUtility.HtmlDecode(entryValue);
                entryValue = System.Text.RegularExpressions.Regex.Replace(entryValue, @"(</?\s*br\s*/?>)", " ");
                entryValue = System.Xml.Linq.XDocument.Parse("<route>" + entryValue + "</route>").Root.Value;
#if DEBUG
                Logger.GetLogger().Info(entryKey + " = " + entryValue);
#endif
            }
#if DEBUG
            catch (Exception e)
#else
            catch (Exception)
#endif
            {
#if DEBUG
                Logger.GetLogger().Warning("Cannot parse config value of key: " + entryKey + ". Value set to " + entryValue + ". " + e.Message);
#endif
            }
            finally
            {
            }

            return entryValue;
        }

        public List<string> LoadLocaleList()
        {
            List<string> res = ResourcesUtils.GetEmbeddedResourceNames(null, ".locale");
            return res;
        }

        public string GetLocaleCode()
        {
            return LocaleCode;
        }

        private void SetLocaleCode()
        {
            LocaleCode = GetConfigValue("DEFAULT_LOCALE", "en");
            string tmpLocale = LocaleCode;

            switch (GetConfigValue("LOCALE").ToLower())
            {
                case "oslang":
                    CultureInfo ci = CultureInfo.CurrentUICulture;
                    string localeCodeCandidate = ci.TwoLetterISOLanguageName;
#if DEBUG
                    Logger.GetLogger().Info("Detected OS display language code: " + localeCodeCandidate);
#endif
                    if (ResourcesUtils.EmbeddedResourceExists(null, localeCodeCandidate + ".locale"))
                        tmpLocale = localeCodeCandidate;
                    break;
                case "userselected":
                    tmpLocale = (string.IsNullOrEmpty(userSelectedLocale)) ? tmpLocale : userSelectedLocale;
                    break;
            }

            if (!ResourcesUtils.EmbeddedResourceExists(null, tmpLocale + ".locale"))
            {
#if DEBUG
                Logger.GetLogger().Info($"requested locale ({tmpLocale}) is missing from resources. default locale will be taken: {LocaleCode}");
#endif
                tmpLocale = LocaleCode;
            }
            LocaleCode = tmpLocale;
            Stream localeXmlStream = ResourcesUtils.GetEmbeddedResourceStream(null, LocaleCode + ".locale");
#if DEBUG            
            Logger.GetLogger().Info($"Chosen locale: {LocaleCode}");
            if (localeXmlStream == null)
                Logger.GetLogger().Fatal($"Chosen locale: {LocaleCode} is missing from resources");

#endif
            ReadXmlFiles(new Stream[] { localeXmlStream });
        }

        internal void SetUserSelectedLocale(string locale)
        {
            userSelectedLocale = locale;
#if DEBUG
            Logger.GetLogger().Info($"user selected locale: {locale}");
#endif
            if (LocaleCode != locale)
            {
                XmlNode oldLocaleNode = xmlDoc?.DocumentElement?.SelectSingleNode("//Locale");
                oldLocaleNode?.ParentNode?.RemoveChild(oldLocaleNode);
                SetLocaleCode();
                SetPagesDesign();
            }
        }

        internal void SetClientSelectedLocale(string locale)
        {
            locale = locale.Trim().ToLower();

            if (int.TryParse(locale, out int localeCode))
            {
                try
                {
                    locale = new CultureInfo(localeCode).TwoLetterISOLanguageName;
                }
                catch (CultureNotFoundException)
                {
#if DEBUG
                    Logger.GetLogger().Error($"SetClientSelectedLocale: {locale} is not a invalid LCID");
#endif
                }

            }
            if (!string.IsNullOrWhiteSpace(locale))
            {
                SetUserSelectedLocale(locale);
            }
        }

        private void ReadConfigSettings()
        {
            SetLocaleCode();
            if (!SetWorkDir(GetConfigValue("WORK_DIR")))
            {
                string md5Name = CryptUtils.ComputeHash(GetConfigValue(ConfigConsts.PRODUCT_TITLE), CryptUtils.Hash.MD5);
                SetWorkDir(Path.Combine(KnownFolders.GetTempFolderSafe(), "Temp\\" + md5Name + "_files\\"));
            }
        }

        private void ReadCmdToXml()
        {
            if (CmdReader.CmdParams == null)
                return;

            if (CmdReader.CmdParams.ContainsKey("override") && File.Exists(CmdReader.CmdParams["override"]))
            {
                Stream overrideStream = File.OpenRead(CmdReader.CmdParams["override"]);

                XmlDocument overrideDoc = new XmlDocument();
                overrideDoc.Load(overrideStream);
                xmlDoc = overrideDoc;
                //XmlParser.SetNode(xmlDoc, overrideDoc.DocumentElement);
            }

        }

        internal void AddRemoteConfig(XmlDocument remoteConfig)
        {
            if (remoteConfig != null && remoteConfig.InnerText != "")
            {

                // add RemoteConfiguration without the products list
                XmlDocument remoteConfigHeader = (XmlDocument)remoteConfig.CloneNode(true);
                remoteConfigHeader?.SelectSingleNode("//RemoteConfiguration")?.RemoveChild(remoteConfigHeader?.SelectSingleNode("//RemoteConfiguration/Products"));

                XmlParser.SetNode(xmlDoc, remoteConfigHeader.DocumentElement);
                XmlNodeList remoteProducts = remoteConfig.SelectNodes("//RemoteConfiguration/Products/Product");

                // add the products lists to Products
                if (remoteProducts.Count > 0)
                {
                    for (int i = (remoteProducts.Count - 1); i >= 0; i--)
                    {
                        XmlNode remoteProduct = remoteProducts[i];
                        XmlParser.SetNode(xmlDoc, xmlDoc.SelectSingleNode("//Products"), remoteProduct, false);
                    }
                }
            }

        }

        private void SetPagesDesign()
        {
            pagesDesign = new List<PageDesign>();
            int pageIndex = 0;
            XmlNodeList PagesList = xmlDoc.SelectNodes("//Flow/Page");
#if DEBUG
            Logger.GetLogger().Info("Read config file - Page Flow:", Logger.Level.MEDIUM_DEBUG_LEVEL);
#endif
            foreach (XmlNode page in PagesList)
            {
                PageDesign pageDesign = new PageDesign
                {
                    PageName = XmlParser.GetStringValue(page, "PageName")
                };

                XmlNodeList controlList = page["Controls"].ChildNodes;
                pageDesign.ControlsLayouts = new ControlsLayout(new XmlNodeList[] { controlList }, formDesign.DefaultControlDesign);
                pageDesign.Index = pageIndex++;
                pagesDesign.Add(pageDesign);
            }
        }

        private void SetFormDesign()
        {
            formDesign = new FormDesign();
            XmlNode formDesignNode = xmlDoc.SelectSingleNode("//FormDesign");
#if DEBUG
            Logger.GetLogger().Info("Read config file - Form Design:", Logger.Level.MEDIUM_DEBUG_LEVEL);
#endif
            if (formDesignNode == null)
                return;

            formDesign.Height = XmlParser.GetIntValue(formDesignNode, "Height");
            formDesign.Width = XmlParser.GetIntValue(formDesignNode, "Width");
            formDesign.ClientHeight = XmlParser.GetIntValue(formDesignNode, "ClientHeight");
            formDesign.ClientWidth = XmlParser.GetIntValue(formDesignNode, "ClientWidth");

            formDesign.BackgroundColor = XmlParser.GetColorValue(formDesignNode, "BackgroundColor");

            formDesign.FormName = XmlParser.GetStringValue(formDesignNode, "FormName");

            formDesign.DefaultControlDesign = XmlParser.GetXpathRefAttributes(formDesignNode.SelectSingleNode("DefaultControlDesign"));
        }

        private bool SetWorkDir(string installerWorkDir)
        {
            try
            {
                if (string.IsNullOrEmpty(workDir))
                {
                    if (string.IsNullOrEmpty(installerWorkDir))
                    {
                        string mainProdName = GetConfigValue(ConfigConsts.PRODUCT_TITLE);
                        mainProdName = string.Join(string.Empty, mainProdName.Split(Path.GetInvalidFileNameChars())); // no invalid filename characters
                        mainProdName = mainProdName.Replace(" ", "_"); // no spaces
                        mainProdName = mainProdName.Substring(0, Math.Min(mainProdName.Length, 15)); // max length 15


                        workDir = Path.Combine(KnownFolders.GetTempFolderSafe(), "Temp\\" + mainProdName + "_files\\");
                    }
                    else
                        workDir = installerWorkDir;

                    if (Directory.Exists(workDir))
                    {
#if DEBUG
                        Logger.GetLogger().Info("workDir " + workDir + " exists already.");
#endif
                    }
                    else
                    {
#if DEBUG
                        Logger.GetLogger().Info("Creating working directory for installer: " + workDir);
#endif
                        Directory.CreateDirectory(workDir);
                    }
                }
            }
#if DEBUG
            catch (Exception e)
#else
            catch (Exception)
#endif
            {
#if DEBUG
                Logger.GetLogger().Error("Creating working directory " + workDir + " failed. " + e.Message);
#endif
                workDir = "";
            }
            return !string.IsNullOrEmpty(workDir);
        }

        private void ReadProductsSettings()
        {
            productsSettings = new List<ProductSettings>();
            foreach (XmlNode productSettingsNode in xmlDoc.SelectNodes("//Products/Product"))
                productsSettings.Add(ExtractProductSettings(productSettingsNode));
        }

        public ProductSettings ExtractProductSettings(XmlNode productSettingsNode)
        {
#if DEBUG
            Logger.GetLogger().Info("Read config file - Product Settings:", Logger.Level.MEDIUM_DEBUG_LEVEL);
#endif            
            var productSettings = new ProductSettings
            {
                IsOptional = XmlParser.GetBoolAttribute(productSettingsNode, "optional"),
                IsExtractable = XmlParser.GetBoolAttribute(productSettingsNode, "extractable", true)
            };

            XmlNode productStaticData = productSettingsNode.SelectSingleNode("StaticData");
            XmlNode productDynamicData = productSettingsNode.SelectSingleNode("DynamicData");
            productSettings.Name = XmlParser.GetStringValue(productStaticData, "Title");
            if (productSettings.IsOptional)
                productSettings.Name = "sec:" + XmlParser.GetStringValue(productDynamicData, "InternalName");
            productSettings.Skin = XmlParser.GetStringValue(productDynamicData, "Skin");
            productSettings.Class = XmlParser.GetStringValue(productDynamicData, "Class");

            EvalCustomVariables(productStaticData.SelectSingleNode("CustomData/CustomVars"));

            productSettings.Filename = XmlParser.GetStringValue(productStaticData, "Filename");
            productSettings.ExtractPath = XmlParser.GetStringValue(productStaticData, "ExtractPath");
            productSettings.RunPath = XmlParser.GetStringValue(productStaticData, "RunPath");

            productSettings.DownloadURLs = new List<ProductSettings.DownloadURL>();
            foreach (XmlNode DownloadURLNode in productStaticData.SelectNodes("DownloadURLs/DownloadURL"))
            {
                ProductSettings.DownloadURL downloadURL = new ProductSettings.DownloadURL
                {
                    Arch = XmlParser.GetStringAttribute(DownloadURLNode, "arch").Trim(),
                    URL = XmlParser.GetStringValue(DownloadURLNode).Trim()
                };
                productSettings.DownloadURLs.Add(downloadURL);
            }

            bool runWithBitsDefault = XmlParser.GetBoolValue(xmlDoc.SelectSingleNode("//Config"), ConfigConsts.RUN_WITH_BITS, true);

            foreach (XmlNode productLogicNode in productStaticData.SelectNodes("Logic"))
            {
                productSettings.Behavior = XmlParser.GetStringValue(productLogicNode, "Behavior");
                productSettings.RunWithBits = XmlParser.GetBoolValue(productLogicNode, "RunWithBits", runWithBitsDefault);								
                productSettings.DownloadMethod = XmlParser.GetStringValue(productLogicNode, "DownloadMethod");
                if (String.IsNullOrEmpty(productSettings.DownloadMethod))
                    productSettings.DownloadMethod = GetConfigValue("DOWNLOAD_METHOD");
                productSettings.SecondaryDownloadMethod = XmlParser.GetStringValue(productLogicNode, "SecondaryDownloadMethod");
                if (String.IsNullOrEmpty(productSettings.SecondaryDownloadMethod))
                    productSettings.SecondaryDownloadMethod = GetConfigValue("SECONDARY_DOWNLOAD_METHOD");
                productSettings.MsiTimeoutMS = XmlParser.GetIntValue(productLogicNode, "MsiTimeoutMs");
                productSettings.ProductEvents = new List<ProductSettings.ProductEvent>();
                foreach (XmlNode EventNode in productLogicNode.SelectNodes("Events/Event"))
                {
                    ProductSettings.ProductEvent productEvent = new ProductSettings.ProductEvent
                    {
                        Type = XmlParser.GetStringValue(EventNode),
                        Triger = XmlParser.GetStringAttribute(EventNode, "reportOn")
                    };
                    productSettings.ProductEvents.Add(productEvent);
                }
            }

            productSettings.RunParams = "";
            foreach (XmlNode runParamsNode in productStaticData.SelectNodes("RunParams/RunParam"))
            {
                string RunParam = XmlParser.GetStringValue(runParamsNode).Trim();

                productSettings.RunParams += (string.IsNullOrEmpty(productSettings.RunParams) ? RunParam : " " + RunParam);
            }
            productSettings.PreInstall = ExtractProductRequirementsRoot(productStaticData.SelectNodes("PreInstall/Requirements"));
            productSettings.PostInstall = ExtractProductRequirementsRoot(productStaticData.SelectNodes("PostInstall/Requirements"));
            productSettings.AnalyticsParams = ExtractProductAnalyticsParams(productStaticData.SelectNodes("AnalyticsParams/Param"));
            productSettings.LayoutName = XmlParser.GetStringValue(productStaticData, "Layout");

            ControlsLayout defLocaleControlsLayout = null;
            productSettings.ControlsLayouts = null;

            XmlNodeList Locales = productSettingsNode.SelectNodes("Locales/Locale");
            if (Locales.Count > 0)
            {
                foreach (XmlNode localeNode in Locales)
                {
                    if (XmlParser.GetBoolAttribute(localeNode, "default"))
                        defLocaleControlsLayout = new ControlsLayout(new XmlNodeList[] { localeNode.SelectNodes("Texts/Text"), localeNode.SelectNodes("Images/Image"), localeNode.SelectNodes("UILayouts") }, formDesign.DefaultControlDesign);
                    string localeLanguage = XmlParser.GetStringAttribute(localeNode, "name");
                    if (localeLanguage == LocaleCode)
                        productSettings.ControlsLayouts = new ControlsLayout(new XmlNodeList[] { localeNode.SelectNodes("Texts/Text"), localeNode.SelectNodes("Images/Image"), localeNode.SelectNodes("UILayouts") }, formDesign.DefaultControlDesign);
                }

                if (productSettings.ControlsLayouts == null)
                    productSettings.ControlsLayouts = defLocaleControlsLayout;

#if DEBUG
                if (productSettings.ControlsLayouts == null)
                    Logger.GetLogger().Error("Missing locale for product: " + productSettings.Name + " language code: " + LocaleCode);
#endif
            }

            return productSettings;
        }

        private ProductSettings.ProductRequirements ExtractProductRequirementsRoot(XmlNodeList productRequirementsList)
        {
            ProductSettings.ProductRequirements requirementsRoot = new ProductSettings.ProductRequirements();
            if (productRequirementsList.Count > 0)
            {
                requirementsRoot = ExtractProductRequirements(productRequirementsList)[0];
            }
            return requirementsRoot;
        }

        private List<ProductSettings.ProductRequirements> ExtractProductRequirements(XmlNodeList productRequirementsList)
        {
            List<ProductSettings.ProductRequirements> requirementsList = new List<ProductSettings.ProductRequirements>();
            foreach (XmlNode requirementsNode in productRequirementsList)
            {
                ProductSettings.ProductRequirements requirements = new ProductSettings.ProductRequirements
                {
                    LogicalOperator = XmlParser.GetStringAttribute(requirementsNode, "Requirements", "logicalOp")
                };
                if (string.IsNullOrEmpty(requirements.LogicalOperator))
                    requirements.LogicalOperator = Enum.GetName(typeof(RequirementHandlers.LogicalOperatorType), RequirementHandlers.LogicalOperatorType.AND); //default value     

                requirements.RequirementList = ExtractProductRequirement(requirementsNode.SelectNodes("Requirement"));
                requirements.RequirementsList = ExtractProductRequirements(requirementsNode.SelectNodes("Requirements"));
                requirementsList.Add(requirements);
            }

            return requirementsList;
        }

        private List<ProductSettings.ProductRequirement> ExtractProductRequirement(XmlNodeList requirementsNode)
        {
            List<ProductSettings.ProductRequirement> requirementList = new List<ProductSettings.ProductRequirement>();

            foreach (XmlNode requirementNode in requirementsNode)
            {
                ProductSettings.ProductRequirement requirement = new ProductSettings.ProductRequirement
                {
                    Type = XmlParser.GetStringValue(requirementNode, "Type"),
                    LogicalOperator = XmlParser.GetStringAttribute(requirementNode, "Keys", "logicalOp"),
                    Delta = XmlParser.GetStringValue(requirementNode, "Delta")
                };
                if (string.IsNullOrEmpty(requirement.LogicalOperator))
                    requirement.LogicalOperator = Enum.GetName(typeof(RequirementHandlers.LogicalOperatorType), RequirementHandlers.LogicalOperatorType.AND); //default value     

                requirement.Keys = new List<ProductSettings.RequirementKey>();
                foreach (XmlNode requirementKey in requirementNode.SelectNodes("Keys/Key"))
                {
                    ProductSettings.RequirementKey reqKey;
                    reqKey.KeyValue = XmlParser.GetStringValue(requirementKey);
                    reqKey.KeyType = XmlParser.GetStringAttribute(requirementKey, "type");
                    requirement.Keys.Add(reqKey);
                }

                requirement.Value = XmlParser.GetStringValue(requirementNode, "Value");
                requirement.ValueOperator = XmlParser.GetStringAttribute(requirementNode, "Value", "compareOp");

                requirementList.Add(requirement);
            }

            return requirementList;
        }

        private Dictionary<string, string> ExtractProductAnalyticsParams(XmlNodeList paramsNodeList)
        {
            Dictionary<string, string> Params = new Dictionary<string, string>();
            foreach (XmlNode paramNode in paramsNodeList)
            {
                string key = XmlParser.GetStringValue(paramNode, "Key");
                string value = XmlParser.GetStringValue(paramNode, "Value");
                Params.Add(key, value);
            }
            return Params;
        }

        public void EvalCustomVariables(XmlNode productCustomVars)
        {
            if (productCustomVars != null && productCustomVars.HasChildNodes)
            {
                RequirementHandlers reqHandlers = new RequirementHandlers();
                foreach (XmlNode CustomVar in productCustomVars.ChildNodes)
                {
                    ProductSettings.ProductRequirement requirement = new ProductSettings.ProductRequirement
                    {
                        Type = XmlParser.GetStringValue(CustomVar, "Type"),
                        LogicalOperator = XmlParser.GetStringAttribute(CustomVar, "Keys", "logicalOp")
                    };
                    if (string.IsNullOrEmpty(requirement.LogicalOperator))
                        requirement.LogicalOperator = Enum.GetName(typeof(RequirementHandlers.LogicalOperatorType), RequirementHandlers.LogicalOperatorType.AND); //default value     

                    requirement.Keys = new List<ProductSettings.RequirementKey>();
                    foreach (XmlNode requirementKey in CustomVar.SelectNodes("Keys/Key"))
                    {
                        ProductSettings.RequirementKey reqKey;
                        reqKey.KeyValue = XmlParser.GetStringValue(requirementKey);
                        reqKey.KeyType = XmlParser.GetStringAttribute(requirementKey, "type");

                        requirement.Keys.Add(reqKey);
                    }
                    string newElementName = XmlParser.GetStringAttribute(CustomVar, "name");
                    if (newElementName != "")
                    {
                        XmlElement elem = xmlDoc.CreateElement(newElementName);
                        elem.InnerText = reqHandlers.EvalRequirement(requirement);
                        productCustomVars.AppendChild(elem);
                    }
                }
            }
        }
        public FormDesign GetFormDesign()
        {
            return formDesign;
        }
        public List<PageDesign> GetPagesDesign()
        {
            return pagesDesign;
        }

        public List<ProductSettings> GetProductsSettings()
        {
            ReadProductsSettings();
            return productsSettings;
        }

        public string GetStringValue(string Xpath, string defaultValue = "")
        {
            string res = defaultValue;
            if (xmlDoc != null)
            {
                XmlNode node = xmlDoc.SelectSingleNode(Xpath);
                if (node != null)
                    res = XmlParser.GetStringValue(node);
            }
            return res;
        }
        public int GetIntValue(string Xpath, int defaultValue = 0)
        {
            int res = defaultValue;
            if (xmlDoc != null)
            {
                XmlNode node = xmlDoc.SelectSingleNode(Xpath);
                if (node != null)
                    res = XmlParser.GetIntValue(node);
            }

            return res;
        }
        public Color GetColorValue(string Xpath)
        {
            Color res = Color.White;
            if (xmlDoc != null)
                res = XmlParser.GetColorValue(xmlDoc.SelectSingleNode(Xpath));
            return res;
        }

        public string GetConfigValue(string key, string defaultValue = "")
        {
            XmlNode configNode = xmlDoc.SelectSingleNode("//Config");
            string res = XmlParser.GetStringValue(configNode, key);
            if (string.IsNullOrEmpty(res))
            {
#if DEBUG
                if (!string.IsNullOrEmpty(defaultValue))
                    Logger.GetLogger().Info("Missing config key (//Config/" + key + "). Setting default value " + defaultValue);
#endif
                res = defaultValue;
            }
            return res;
        }

        public void SetStringValue(string xpath, string value)
        {
            if (xmlDoc != null)
                XmlParser.SetStringValue(xmlDoc, xpath, value);
        }
    }
}
