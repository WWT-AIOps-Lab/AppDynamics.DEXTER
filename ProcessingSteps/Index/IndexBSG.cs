﻿using AppDynamics.Dexter.ReportObjectMaps;
using AppDynamics.Dexter.ReportObjects;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AppDynamics.Dexter.ProcessingSteps
{
    public class IndexBSG_APM : JobStepIndexBase
    {
        public override bool Execute(ProgramOptions programOptions, JobConfiguration jobConfiguration)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            StepTiming stepTimingFunction = new StepTiming();
            stepTimingFunction.JobFileName = programOptions.OutputJobFilePath;
            stepTimingFunction.StepName = jobConfiguration.Status.ToString();
            stepTimingFunction.StepID = (int)jobConfiguration.Status;
            stepTimingFunction.StartTime = DateTime.Now;
            stepTimingFunction.NumEntities = jobConfiguration.Target.Count;

            this.DisplayJobStepStartingStatus(jobConfiguration);

            FilePathMap = new FilePathMap(programOptions, jobConfiguration);

            try
            {
                if (this.ShouldExecute(programOptions, jobConfiguration) == false)
                {
                    return true;
                }

                if (jobConfiguration.Target.Count(t => t.Type == APPLICATION_TYPE_APM) == 0)
                {
                    logger.Warn("No {0} targets to process", APPLICATION_TYPE_APM);
                    loggerConsole.Warn("No {0} targets to process", APPLICATION_TYPE_APM);

                    return true;
                }

                bool reportFolderCleaned = false;

                // Process each target
                for (int i = 0; i < jobConfiguration.Target.Count; i++)
                {
                    Stopwatch stopWatchTarget = new Stopwatch();
                    stopWatchTarget.Start();

                    JobTarget jobTarget = jobConfiguration.Target[i];

                    if (jobTarget.Type != null && jobTarget.Type.Length > 0 && jobTarget.Type != APPLICATION_TYPE_APM) continue;

                    StepTiming stepTimingTarget = new StepTiming();
                    stepTimingTarget.Controller = jobTarget.Controller;
                    stepTimingTarget.ApplicationName = jobTarget.Application;
                    stepTimingTarget.ApplicationID = jobTarget.ApplicationID;
                    stepTimingTarget.JobFileName = programOptions.OutputJobFilePath;
                    stepTimingTarget.StepName = jobConfiguration.Status.ToString();
                    stepTimingTarget.StepID = (int)jobConfiguration.Status;
                    stepTimingTarget.StartTime = DateTime.Now;

                    try
                    {
                        this.DisplayJobTargetStartingStatus(jobConfiguration, jobTarget, i + 1);
                        
                        XmlDocument configXml = FileIOHelper.LoadXmlDocumentFromFile(FilePathMap.APMApplicationConfigurationXMLDataFilePath(jobTarget));
                        if (configXml == null)
                        {
                            logger.Warn("No application configuration in {0} file", FilePathMap.APMApplicationConfigurationXMLDataFilePath(jobTarget));
                            loggerConsole.Warn("No application configuration in {0} file", FilePathMap.APMApplicationConfigurationXMLDataFilePath(jobTarget));
                            logger.Warn(("Skipping BSG report for application {0}", jobTarget.Application));
                            loggerConsole.Warn(("Skipping BSG report for application {0}", jobTarget.Application));
                            continue;
                        }

                        #region Target step variables

                        List<BSGAgentResult> bsgAgentResults = new List<BSGAgentResult>();
                        List<BSGBackendResult> bsgBackendResults = new List<BSGBackendResult>();
                        List<BSGBusinessTransactionResult> bsgBTResults = new List<BSGBusinessTransactionResult>();
                        List<BSGOverheadResult> bsgOverheadResults = new List<BSGOverheadResult>();
                        List<BSGDataCollectorResult> bsgDataCollectorResults = new List<BSGDataCollectorResult>();
                        List<BSGDashboardResult> bsgDashboardResults = new List<BSGDashboardResult>();
                        List<BSGErrorsResult> bsgErrorsResults = new List<BSGErrorsResult>();

                        #endregion

                        #region Preload all the reports that will be filtered by the subsequent entities

                        loggerConsole.Info("Entity Details Data Preloading");
                        
                        List<ControllerSummary> controllerSummariesList = FileIOHelper.ReadListFromCSVFile<ControllerSummary>(FilePathMap.ControllerSummaryIndexFilePath(jobTarget), new ControllerSummaryReportMap());
                        List<ControllerSetting> controllerSettingsList = FileIOHelper.ReadListFromCSVFile<ControllerSetting>(FilePathMap.ControllerSettingsIndexFilePath(jobTarget), new ControllerSettingReportMap());

                        List<APMApplication> applicationsList = FileIOHelper.ReadListFromCSVFile<APMApplication>(FilePathMap.APMApplicationsIndexFilePath(jobTarget), new APMApplicationReportMap());
                        List<APMApplication> applicationsMetricsList = FileIOHelper.ReadListFromCSVFile<APMApplication>(FilePathMap.APMEntitiesFullIndexFilePath(jobTarget, APMApplication.ENTITY_FOLDER), new ApplicationMetricReportMap());

                        List<APMTier> tiersList = FileIOHelper.ReadListFromCSVFile<APMTier>(FilePathMap.APMTiersIndexFilePath(jobTarget), new APMTierReportMap());
                        List<APMTier> tiersMetricsList = FileIOHelper.ReadListFromCSVFile<APMTier>(FilePathMap.APMEntitiesFullIndexFilePath(jobTarget, APMTier.ENTITY_FOLDER), new TierMetricReportMap());

                        List<APMNode> nodesList = FileIOHelper.ReadListFromCSVFile<APMNode>(FilePathMap.APMNodesIndexFilePath(jobTarget), new APMNodeReportMap());
                        List<APMNode> nodesMetricsList = FileIOHelper.ReadListFromCSVFile<APMNode>(FilePathMap.APMEntitiesFullIndexFilePath(jobTarget, APMNode.ENTITY_FOLDER), new NodeMetricReportMap());

                        List<APMBusinessTransaction> businessTransactionsList = FileIOHelper.ReadListFromCSVFile<APMBusinessTransaction>(FilePathMap.APMBusinessTransactionsIndexFilePath(jobTarget), new APMBusinessTransactionReportMap());
                        List<APMBusinessTransaction> businessTransactionsMetricsList = FileIOHelper.ReadListFromCSVFile<APMBusinessTransaction>(FilePathMap.APMEntitiesFullIndexFilePath(jobTarget, APMBusinessTransaction.ENTITY_FOLDER), new BusinessTransactionMetricReportMap());
                        List<APMBusinessTransaction> businessTransactionsOverflowList = FileIOHelper.ReadListFromCSVFile<APMBusinessTransaction>(FilePathMap.APMOverflowBusinessTransactionsIndexFilePath(jobTarget), new APMOverflowBusinessTransactionReportMap());

                        List<APMBackend> backendsList = FileIOHelper.ReadListFromCSVFile<APMBackend>(FilePathMap.APMBackendsIndexFilePath(jobTarget), new APMBackendReportMap());
                        List<APMBackend> backendsMetricsList = FileIOHelper.ReadListFromCSVFile<APMBackend>(FilePathMap.APMEntitiesFullIndexFilePath(jobTarget, APMBackend.ENTITY_FOLDER), new BackendMetricReportMap());

                        List<APMServiceEndpoint> serviceEndpointsList = FileIOHelper.ReadListFromCSVFile<APMServiceEndpoint>(FilePathMap.APMServiceEndpointsIndexFilePath(jobTarget), new APMServiceEndpointReportMap());
                        List<APMServiceEndpoint> serviceEndpointsMetricsList = FileIOHelper.ReadListFromCSVFile<APMServiceEndpoint>(FilePathMap.APMEntitiesFullIndexFilePath(jobTarget, APMServiceEndpoint.ENTITY_FOLDER), new ServiceEndpointMetricReportMap());

                        List<APMError> errorsList = FileIOHelper.ReadListFromCSVFile<APMError>(FilePathMap.APMErrorsIndexFilePath(jobTarget), new APMErrorReportMap());
                        List<APMError> errorsMetricsList = FileIOHelper.ReadListFromCSVFile<APMError>(FilePathMap.APMEntitiesFullIndexFilePath(jobTarget, APMError.ENTITY_FOLDER), new ErrorMetricReportMap());

                        List<APMInformationPoint> informationPointsList = FileIOHelper.ReadListFromCSVFile<APMInformationPoint>(FilePathMap.APMInformationPointsIndexFilePath(jobTarget), new APMInformationPointReportMap());
                        List<APMInformationPoint> informationPointsMetricsList = FileIOHelper.ReadListFromCSVFile<APMInformationPoint>(FilePathMap.APMEntitiesFullIndexFilePath(jobTarget, APMInformationPoint.ENTITY_FOLDER), new InformationPointMetricReportMap());

                        List<APMResolvedBackend> resolvedBackendsList = FileIOHelper.ReadListFromCSVFile<APMResolvedBackend>(FilePathMap.APMMappedBackendsIndexFilePath(jobTarget), new APMResolvedBackendReportMap());

                        //List<Event> eventsAllList = FileIOHelper.ReadListFromCSVFile<Event>(FilePathMap.ApplicationEventsIndexFilePath(jobTarget), new EventReportMap());
                        List<HealthRuleViolationEvent> healthRuleViolationEventsAllList = FileIOHelper.ReadListFromCSVFile<HealthRuleViolationEvent>(FilePathMap.ApplicationHealthRuleViolationsIndexFilePath(jobTarget), new HealthRuleViolationEventReportMap());
                        List<ApplicationConfigurationPolicy> applicationConfigurationsListReference = FileIOHelper.ReadListFromCSVFile<ApplicationConfigurationPolicy>(FilePathMap.ApplicationConfigurationHealthRulesIndexFilePath(jobTarget), new ApplicationConfigurationPolicyReportMap());
                        List<HealthRule> healthRulesListReference = FileIOHelper.ReadListFromCSVFile<HealthRule>(FilePathMap.ApplicationHealthRulesIndexFilePath(jobConfiguration.Input.ConfigurationComparisonReferenceAPM), new HealthRuleReportMap());
                        List<HealthRule> healthRulesListDifference = FileIOHelper.ReadListFromCSVFile<HealthRule>(FilePathMap.ApplicationHealthRulesIndexFilePath(jobTarget), new HealthRuleReportMap());
                        //List<Snapshot> snapshotsAllList = FileIOHelper.ReadListFromCSVFile<Snapshot>(FilePathMap.SnapshotsIndexFilePath(jobTarget), new SnapshotReportMap());
                        //List<Segment> segmentsAllList = FileIOHelper.ReadListFromCSVFile<Segment>(FilePathMap.SnapshotsSegmentsIndexFilePath(jobTarget), new SegmentReportMap());
                        //List<ExitCall> exitCallsAllList = FileIOHelper.ReadListFromCSVFile<ExitCall>(FilePathMap.SnapshotsExitCallsIndexFilePath(jobTarget), new ExitCallReportMap());
                        //List<ServiceEndpointCall> serviceEndpointCallsAllList = FileIOHelper.ReadListFromCSVFile<ServiceEndpointCall>(FilePathMap.SnapshotsServiceEndpointCallsIndexFilePath(jobTarget), new ServiceEndpointCallReportMap());
                        //List<DetectedError> detectedErrorsAllList = FileIOHelper.ReadListFromCSVFile<DetectedError>(FilePathMap.SnapshotsDetectedErrorsIndexFilePath(jobTarget), new DetectedErrorReportMap());
                        //List<BusinessData> businessDataAllList = FileIOHelper.ReadListFromCSVFile<BusinessData>(FilePathMap.SnapshotsBusinessDataIndexFilePath(jobTarget), new BusinessDataReportMap());

                        List<AgentConfigurationProperty> agentPropertiesList = FileIOHelper.ReadListFromCSVFile<AgentConfigurationProperty>(FilePathMap.APMAgentConfigurationPropertiesIndexFilePath(jobTarget), new AgentConfigurationPropertyReportMap());


                        loggerConsole.Info("Configuration Rules Data Preloading");

                        List<APMApplicationConfiguration> applicationConfigurationsList = FileIOHelper.ReadListFromCSVFile<APMApplicationConfiguration>(FilePathMap.APMApplicationConfigurationIndexFilePath(jobTarget), new APMApplicationConfigurationReportMap());
                        List<AgentCallGraphSetting> agentCallGraphSettings = FileIOHelper.ReadListFromCSVFile<AgentCallGraphSetting>(FilePathMap.APMAgentCallGraphSettingsIndexFilePath(jobTarget), new AgentCallGraphSettingReportMap());

                        List<BusinessTransactionEntryScope> businessTransactionEntryScopesList = FileIOHelper.ReadListFromCSVFile<BusinessTransactionEntryScope>(FilePathMap.APMBusinessTransactionEntryScopesIndexFilePath(jobTarget), new BusinessTransactionEntryScopeReportMap());
                        List<BusinessTransactionDiscoveryRule20> businessTransactionDiscoveryRules20List = FileIOHelper.ReadListFromCSVFile<BusinessTransactionDiscoveryRule20>(FilePathMap.APMBusinessTransactionDiscoveryRules20IndexFilePath(jobTarget), new BusinessTransactionDiscoveryRule20ReportMap());
                        List<BusinessTransactionEntryRule> businessTransactionEntryRulesList = FileIOHelper.ReadListFromCSVFile<BusinessTransactionEntryRule>(FilePathMap.APMBusinessTransactionEntryRulesIndexFilePath(jobTarget), new BusinessTransactionEntryRuleReportMap());
                        List<BusinessTransactionEntryRule20> businessTransactionEntryRules20List = FileIOHelper.ReadListFromCSVFile<BusinessTransactionEntryRule20>(FilePathMap.APMBusinessTransactionEntryRules20IndexFilePath(jobTarget), new BusinessTransactionEntryRule20ReportMap());
                        List<BackendDiscoveryRule> backendDiscoveryRulesList = FileIOHelper.ReadListFromCSVFile<BackendDiscoveryRule>(FilePathMap.APMBackendDiscoveryRulesIndexFilePath(jobTarget), new BackendDiscoveryRuleReportMap());
                        List<CustomExitRule> customExitRulesList = FileIOHelper.ReadListFromCSVFile<CustomExitRule>(FilePathMap.APMCustomExitRulesIndexFilePath(jobTarget), new CustomExitRuleReportMap());
                        List<ServiceEndpointEntryRule> sepDiscoveryRules = FileIOHelper.ReadListFromCSVFile<ServiceEndpointEntryRule>(FilePathMap.APMServiceEndpointEntryRulesIndexFilePath(jobTarget), new ServiceEndpointEntryRuleReportMap());
                        List<ServiceEndpointDiscoveryRule> serviceEndpointDiscoveryDefaultRules = FileIOHelper.ReadListFromCSVFile<ServiceEndpointDiscoveryRule>(FilePathMap.APMServiceEndpointDiscoveryRulesIndexFilePath(jobTarget), new ServiceEndpointDiscoveryRuleReportMap());
                        
                        List<HTTPDataCollector> httpDataCollectors = FileIOHelper.ReadListFromCSVFile<HTTPDataCollector>(FilePathMap.APMHttpDataCollectorsIndexFilePath(jobTarget), new HTTPDataCollectorReportMap());
                        List<MethodInvocationDataCollector> midcDataCollectors = FileIOHelper.ReadListFromCSVFile<MethodInvocationDataCollector>(FilePathMap.APMMethodInvocationDataCollectorsIndexFilePath(jobTarget), new MethodInvocationDataCollectorReportMap());

                        List<Dashboard> dashboardsList = FileIOHelper.ReadListFromCSVFile<Dashboard>(FilePathMap.DashboardsIndexFilePath(jobTarget), new DashboardReportMap());
                        
                        List<BusinessTransactionEntryScope> businessTransactionEntryScopeTemplateList = null;
                        List<BusinessTransactionDiscoveryRule20> businessTransactionDiscoveryRules20TemplateList = null;
                        List<BusinessTransactionEntryRule20> businessTransactionEntryRules20TemplateList = null;
                        
                        List<ErrorDetectionRule> errorDetectionRulesList = FileIOHelper.ReadListFromCSVFile<ErrorDetectionRule>(FilePathMap.APMErrorDetectionRulesIndexFilePath(jobTarget), new ErrorDetectionRuleReportMap());
                        
                        // Load the template configuration of the APM rules to compare
                        // Focus is on the blank template comparing from the defaults, so the configuration comparison to some real app should be off
                        if (jobConfiguration.Input.ConfigurationComparisonReferenceAPM.Controller == BLANK_APPLICATION_CONTROLLER &&
                            jobConfiguration.Input.ConfigurationComparisonReferenceAPM.Application == BLANK_APPLICATION_APM)
                        {
                            businessTransactionEntryScopeTemplateList = FileIOHelper.ReadListFromCSVFile<BusinessTransactionEntryScope>(FilePathMap.APMBusinessTransactionEntryScopesIndexFilePath(jobConfiguration.Input.ConfigurationComparisonReferenceAPM), new BusinessTransactionEntryScopeReportMap());
                            businessTransactionDiscoveryRules20TemplateList = FileIOHelper.ReadListFromCSVFile<BusinessTransactionDiscoveryRule20>(FilePathMap.APMBusinessTransactionDiscoveryRules20IndexFilePath(jobConfiguration.Input.ConfigurationComparisonReferenceAPM), new BusinessTransactionDiscoveryRule20ReportMap());
                            businessTransactionEntryRules20TemplateList = FileIOHelper.ReadListFromCSVFile<BusinessTransactionEntryRule20>(FilePathMap.APMBusinessTransactionEntryRules20IndexFilePath(jobConfiguration.Input.ConfigurationComparisonReferenceAPM), new BusinessTransactionEntryRule20ReportMap());
                        }

                        List<ConfigurationDifference> configurationDifferencesList = FileIOHelper.ReadListFromCSVFile<ConfigurationDifference>(FilePathMap.ConfigurationComparisonIndexFilePath(jobTarget), new ConfigurationDifferenceReportMap());

                        #endregion
                        
                        #region Agents
                        
                        foreach (var node in nodesMetricsList)
                        {
                            // nodesMetricsList does not have agent version info
                            var apmNode = nodesList.Find(e => e.NodeID == node.NodeID);
                            BSGAgentResult agentResult = new BSGAgentResult();
                            agentResult.Controller = node.Controller;
                            agentResult.ApplicationID = node.ApplicationID;
                            agentResult.ApplicationName = node.ApplicationName;
                            agentResult.TierName = node.TierName;
                            agentResult.NodeName = node.NodeName;
                            agentResult.AgentVersion = apmNode.AgentVersion;
                            agentResult.IsAPMAgentUsed = node.IsAPMAgentUsed;
                            agentResult.IsMachineAgentUsed = node.IsMachineAgentUsed;
                            agentResult.MachineAgentVersion = apmNode.MachineAgentVersion;

                            bsgAgentResults.Add(agentResult);
                        }
                        bsgAgentResults.RemoveAll(h => h == null);
                        // Sort them
                        bsgAgentResults = bsgAgentResults.OrderBy(h => h.Controller).ThenBy(h => h.ApplicationName).ThenBy(h => h.TierName).ThenBy(h => h.NodeName).ToList();

                        #endregion

                        #region BusinessTransactions
                        foreach(var applicationConfiguration in applicationConfigurationsList)
                        {
                            BSGBusinessTransactionResult businessTransactionResult = new BSGBusinessTransactionResult();
                            businessTransactionResult.Controller = applicationConfiguration.Controller;
                            businessTransactionResult.ApplicationName = applicationConfiguration.ApplicationName;
                            businessTransactionResult.BTLockdownEnabled = applicationConfiguration.IsBTLockdownEnabled;
                            businessTransactionResult.BTCleanupEnabled = applicationConfiguration.IsBTCleanupEnabled;
                            businessTransactionResult.NumBTs = applicationConfiguration.NumBTs;

                            businessTransactionResult.NumBTsWithLoad =
                                businessTransactionsMetricsList.FindAll(b => b.HasActivity == true).Count;
                            businessTransactionResult.NumBTsOverflow = businessTransactionsOverflowList.Count;

                            if (businessTransactionsOverflowList.Count > 0)
                                businessTransactionResult.BTLimitExceeded = true;

                            businessTransactionResult.NumBTEntryRules = applicationConfiguration.NumBTEntryRules;
                            businessTransactionResult.NumBTExcludeRules = applicationConfiguration.NumBTExcludeRules;
                            businessTransactionResult.NumBTCustomEntryRules =
                                        businessTransactionEntryRulesList.FindAll(e => e.IsBuiltIn == false).Count;
                            businessTransactionResult.NumBTCustomExcludeRules =
                                        businessTransactionEntryRulesList.FindAll(e => e.IsBuiltIn == false && e.IsExclusion == true).Count;
                            businessTransactionResult.NumBTDiscoveryRules = applicationConfiguration.NumBTDiscoveryRules;
                            

                            businessTransactionResult.NumBT20EntryRules = applicationConfiguration.NumBT20EntryRules;
                            businessTransactionResult.NumBT20ExcludeRules = applicationConfiguration.NumBT20ExcludeRules;
                            businessTransactionResult.NumBT20CustomEntryRules =
                                        businessTransactionEntryRules20List.FindAll(e => e.IsBuiltIn == false).Count;
                            businessTransactionResult.NumBT20CustomExcludeRules =
                                        businessTransactionEntryRules20List.FindAll(e => e.IsBuiltIn == false && e.IsExclusion == true).Count;
                            businessTransactionResult.NumBT20DiscoveryRules = applicationConfiguration.NumBT20DiscoveryRules;
                            businessTransactionResult.NumBT20Scopes = applicationConfiguration.NumBT20Scopes;

                            bsgBTResults.Add(businessTransactionResult);
                        } 
                        bsgBTResults.RemoveAll(h => h == null);
                        bsgBTResults = bsgBTResults.OrderBy(h => h.Controller).ThenBy(h => h.ApplicationName).ToList();
                        #endregion

                        #region DataCollectors

                        foreach (var applicationConfiguration in applicationConfigurationsList)
                        {
                            BSGDataCollectorResult bsgDataCollector = new BSGDataCollectorResult();
                            bsgDataCollector.Controller = applicationConfiguration.Controller;
                            bsgDataCollector.ApplicationName = applicationConfiguration.ApplicationName;
                            bsgDataCollector.ApplicationID = applicationConfiguration.ApplicationID;
                            bsgDataCollector.NumHTTPDCs = applicationConfiguration.NumHTTPDCs;
                            bsgDataCollector.NumHTTPDCVariablesCollected = applicationConfiguration.NumHTTPDCVariablesCollected;
                            bsgDataCollector.NumHTTPDCs_BIQEnabled = httpDataCollectors.FindAll(m => m.IsAnalytics == true).Count();

                            bsgDataCollector.NumMIDCs = applicationConfiguration.NumMIDCs;
                            bsgDataCollector.NumMIDCVariablesCollected = applicationConfiguration.NumMIDCVariablesCollected;
                            bsgDataCollector.NumMIDCs_BIQEnabled = midcDataCollectors.FindAll(m => m.IsAnalytics == true).Count();
                            bsgDataCollectorResults.Add(bsgDataCollector);
                        }
                        bsgDataCollectorResults.RemoveAll(h => h == null);
                        // Sort them
                        bsgDataCollectorResults = bsgDataCollectorResults.OrderBy(h => h.Controller).ThenBy(h => h.ApplicationName).ToList();

                        #endregion

                        #region Backends

                        foreach (var backend in backendsMetricsList)
                        {
                            BSGBackendResult backendResult = new BSGBackendResult();
                            backendResult.Controller = backend.Controller;
                            backendResult.ApplicationName = backend.ApplicationName;
                            backendResult.BackendName = backend.BackendName;
                            backendResult.BackendType = backend.BackendType;
                            backendResult.HasActivity = backend.HasActivity;
                            bsgBackendResults.Add(backendResult);
                        }
                        bsgBackendResults.RemoveAll(h => h == null);
                        // Sort them
                        bsgBackendResults = bsgBackendResults.OrderBy(h => h.Controller).ThenBy(h => h.ApplicationName).ThenBy(h => h.BackendName).ToList();
                        
                        #endregion
                        
                        #region Backend Customization
                        
                        var backendCustomizationResult = new BSGBackendCustomizationResult();
                        backendCustomizationResult.Controller = jobTarget.Controller;
                        backendCustomizationResult.ApplicationName = jobTarget.Application;
                        backendCustomizationResult.CustomDiscoveryRules =
                            backendDiscoveryRulesList.FindAll(e => e.IsBuiltIn == false).Count;
                        backendCustomizationResult.CustomExitPoints = customExitRulesList.Count;
                        var bsgBackendCustomizationResults = new List<BSGBackendCustomizationResult>();
                        bsgBackendCustomizationResults.Add(backendCustomizationResult);
                        
                        #endregion

                        #region Service Endpoints

                        var bsgSepResults = new List<BSGSepResult>();

                        
                        BSGSepResult sepResult = new BSGSepResult();
                        sepResult.Controller = jobTarget.Controller;
                        sepResult.ApplicationName = jobTarget.Application;
                        sepResult.NumServiceEndpoints = serviceEndpointsList.Count;
                        sepResult.NumServiceEndpointsWithLoad =
                            serviceEndpointsMetricsList.FindAll(e => e.HasActivity).Count;
                        sepResult.NumServiceEndpointDetectionRules = sepDiscoveryRules.Count;
                        sepResult.EjbAutoDiscoveryEnabled = serviceEndpointDiscoveryDefaultRules
                            .Find(e => e.EntryPointType.Equals("EJB")).IsEnabled;
                        sepResult.JmsAutoDiscoveryEnabled = serviceEndpointDiscoveryDefaultRules
                            .Find(e => e.EntryPointType.Equals("JMS")).IsEnabled;
                        sepResult.PojoAutoDiscoveryEnabled = serviceEndpointDiscoveryDefaultRules
                            .Find(e => e.EntryPointType.Equals("POJO")).IsEnabled;
                        sepResult.ServletAutoDiscoveryEnabled = serviceEndpointDiscoveryDefaultRules
                            .Find(e => e.EntryPointType.Equals("SERVLET")).IsEnabled;
                        sepResult.SpringBeanAutoDiscoveryEnabled = serviceEndpointDiscoveryDefaultRules
                            .Find(e => e.EntryPointType.Equals("SPRING_BEAN")).IsEnabled;
                        sepResult.StrutsAutoDiscoveryEnabled = serviceEndpointDiscoveryDefaultRules
                            .Find(e => e.EntryPointType.Equals("STRUTS_ACTION")).IsEnabled;
                        sepResult.WebServiceAutoDiscoveryEnabled = serviceEndpointDiscoveryDefaultRules
                            .Find(e => e.EntryPointType.Equals("WEB_SERVICE")).IsEnabled;
                        bsgSepResults.Add(sepResult);

                        #endregion

                        #region Overhead

                        foreach (var applicationConfiguration in applicationConfigurationsList)
                        {
                            BSGOverheadResult bsgOverhead = new BSGOverheadResult();
                            bsgOverhead.Controller = applicationConfiguration.Controller;
                            bsgOverhead.ApplicationName = applicationConfiguration.ApplicationName;
                            bsgOverhead.ApplicationID = applicationConfiguration.ApplicationID;
                            bsgOverhead.DeveloperModeEnabled = applicationConfiguration.IsDeveloperModeEnabled;
                            bsgOverhead.FindEntryPointsEnabled =
                                agentPropertiesList
                                    .FindAll(n => n.ApplicationID.Equals(applicationConfiguration.ApplicationID))
                                    .Where(n => n.PropertyName == "find-entry-points" && n.BooleanValue == true)
                                    .Count() > 0;
                            bsgOverhead.SlowSnapshotCollectionEnabled =
                                agentCallGraphSettings
                                    .FindAll(c => c.IsHotSpotEnabled == true)
                                    .Count() > 0;

                            bsgOverheadResults.Add(bsgOverhead);
                        }
                        bsgOverheadResults.RemoveAll(h => h == null);
                        // Sort them
                        bsgOverheadResults = bsgOverheadResults.OrderBy(h => h.Controller).ThenBy(h => h.ApplicationName).ToList();

                        #endregion

                        #region Health Rules

                        BSGHealthRuleResult healthRuleResult = new BSGHealthRuleResult();
                        healthRuleResult.Controller = jobTarget.Controller;
                        healthRuleResult.ApplicationName = jobTarget.Application;
                        healthRuleResult.WarningViolations = healthRuleViolationEventsAllList
                            .FindAll(e => e.Severity.Equals("WARNING")).Count;
                        healthRuleResult.CriticalViolations = healthRuleViolationEventsAllList
                            .FindAll(e => e.Severity.Equals("CRITICAL")).Count;
                        healthRuleResult.LinkedActions = applicationConfigurationsListReference.First().NumActions;
                        healthRuleResult.LinkedPolicies = applicationConfigurationsListReference.First().NumPolicies;

                        var configuredHealthRuleNames = healthRulesListDifference.ConvertAll(e => e.RuleName).ToHashSet();
                        var defaultHealthRuleNames = healthRulesListReference.ConvertAll(e => e.RuleName).ToHashSet();
                        
                        var intersect = configuredHealthRuleNames.Intersect(defaultHealthRuleNames).ToList();
                        healthRuleResult.CustomHealthRules = configuredHealthRuleNames.Count - intersect.Count;

                        var allHealthRuleModifications = compareListOfEntities(jobConfiguration.Input.ConfigurationComparisonReferenceAPM,
                            jobTarget, healthRulesListReference, healthRulesListDifference);
                        var modifiedHealthRuleNames = allHealthRuleModifications.ConvertAll(e => e.EntityName).ToHashSet();
                        healthRuleResult.DefaultHealthRulesModified = modifiedHealthRuleNames.Intersect(defaultHealthRuleNames).ToList().Count;
                        
                        var bsgHealthRuleResults = new List<BSGHealthRuleResult>();
                        bsgHealthRuleResults.Add(healthRuleResult);

                        #endregion

                        #region Dashboards
                        foreach(var dashboard in dashboardsList)
                        {
                            BSGDashboardResult bsgDashboardResult = new BSGDashboardResult();
                            bsgDashboardResult.Controller = jobTarget.Controller;
                            bsgDashboardResult.DashboardName = dashboard.DashboardName;
                            bsgDashboardResult.LastModifiedOn = dashboard.UpdatedOn;
                            bsgDashboardResult.LastModifiedOnUtc = dashboard.UpdatedOnUtc;
                            bsgDashboardResult.DaysSince_LastUpdate = (DateTime.Now.ToUniversalTime() - bsgDashboardResult.LastModifiedOnUtc).Days;
                            bsgDashboardResult.HasAnalyticsWidgets = dashboard.NumAnalyticsWidgets > 0;
                            bsgDashboardResult.NumWidgets = dashboard.NumWidgets;
                            bsgDashboardResult.NumAnalyticsWidgets = dashboard.NumAnalyticsWidgets;
                            bsgDashboardResults.Add(bsgDashboardResult);
                            
                        }
                        bsgDashboardResults.RemoveAll(h => h == null);
                        // Sort them
                        bsgDashboardResults = bsgDashboardResults.OrderBy(h => h.Controller).ThenBy(h => h.DashboardName).ToList();

                        #endregion
                        
                        #region Errors

                        BSGErrorsResult bsgErrorsResult = new BSGErrorsResult();
                        bsgErrorsResult.Controller = jobTarget.Controller;
                        bsgErrorsResult.ApplicationName = jobTarget.Application;
                        bsgErrorsResult.ApplicationID = jobTarget.ApplicationID;
                        bsgErrorsResult.MaxErrorRate = errorsList.Count;
                        bsgErrorsResult.NumDetectionRules = errorDetectionRulesList.Count;
                        bsgErrorsResults.Add(bsgErrorsResult);
                        
                        #endregion

                        // Set version to each of the health check rule results
                        string versionOfDEXTER = Assembly.GetEntryAssembly().GetName().Version.ToString();

                        FileIOHelper.WriteListToCSVFile(bsgAgentResults, new BSGAgentResultMap(), FilePathMap.BSGAgentResultsIndexFilePath(jobTarget));
                        FileIOHelper.WriteListToCSVFile(bsgBTResults, new BSGBusinessTransactionResultMap(), FilePathMap.BSGBusinessTransactionResultsIndexFilePath(jobTarget));
                        FileIOHelper.WriteListToCSVFile(bsgBackendResults, new BSGBackendResultMap(), FilePathMap.BSGBackendResultsIndexFilePath(jobTarget));
                        FileIOHelper.WriteListToCSVFile(bsgBackendCustomizationResults, new BSGBackendCustomizationResultMap(), FilePathMap.BSGBackendCustomizationResultsIndexFilePath(jobTarget));
                        FileIOHelper.WriteListToCSVFile(bsgSepResults, new BSGSepResultMap(), FilePathMap.BSGSepResultsIndexFilePath(jobTarget));
                        FileIOHelper.WriteListToCSVFile(bsgOverheadResults, new BSGOverheadResultMap(), FilePathMap.BSGOverheadResultsIndexFilePath(jobTarget));
                        FileIOHelper.WriteListToCSVFile(bsgHealthRuleResults, new BSGHealthRuleResultMap(), FilePathMap.BSGHealthRuleResultsIndexFilePath(jobTarget));
                        FileIOHelper.WriteListToCSVFile(bsgDataCollectorResults, new BSGDataCollectorResultMap(), FilePathMap.BSGDataCollectorResultsIndexFilePath(jobTarget));
                        FileIOHelper.WriteListToCSVFile(bsgDashboardResults, new BSGDashboardResultMap(), FilePathMap.BSGDashboardResultsIndexFilePath(jobTarget));
                        FileIOHelper.WriteListToCSVFile(bsgErrorsResults, new BSGErrorsResultMap(), FilePathMap.BSGErrorsResultsIndexFilePath(jobTarget));

                        stepTimingTarget.NumEntities = bsgAgentResults.Count;

                        #region Combine All for Report CSV

                        // If it is the first one, clear out the combined folder
                        if (reportFolderCleaned == false)
                        {
                            FileIOHelper.DeleteFolder(FilePathMap.BSGReportFolderPath());
                            Thread.Sleep(1000);
                            FileIOHelper.CreateFolder(FilePathMap.BSGReportFolderPath());
                            reportFolderCleaned = true;
                        }

                        // Append all the individual report files into one
                        if (File.Exists(FilePathMap.BSGAgentResultsIndexFilePath(jobTarget)) == true && new FileInfo(FilePathMap.BSGAgentResultsIndexFilePath(jobTarget)).Length > 0)
                        {
                            FileIOHelper.AppendTwoCSVFiles(FilePathMap.BSGAgentResultsExcelReportFilePath(), FilePathMap.BSGAgentResultsIndexFilePath(jobTarget));
                        }

                        // Append all the individual report files into one
                        if (File.Exists(FilePathMap.BSGBusinessTransactionResultsIndexFilePath(jobTarget)) == true && new FileInfo(FilePathMap.BSGBusinessTransactionResultsIndexFilePath(jobTarget)).Length > 0)
                        {
                            FileIOHelper.AppendTwoCSVFiles(FilePathMap.BSGBusinessTransactionExcelReportFilePath(), FilePathMap.BSGBusinessTransactionResultsIndexFilePath(jobTarget));
                        }

                        // Append all the individual report files into one
                        if (File.Exists(FilePathMap.BSGBackendResultsIndexFilePath(jobTarget)) == true && new FileInfo(FilePathMap.BSGBackendResultsIndexFilePath(jobTarget)).Length > 0)
                        {
                            FileIOHelper.AppendTwoCSVFiles(FilePathMap.BSGBackendResultsExcelReportFilePath(), FilePathMap.BSGBackendResultsIndexFilePath(jobTarget));
                        }
                        // Append all the individual report files into one
                        if (File.Exists(FilePathMap.BSGBackendCustomizationResultsIndexFilePath(jobTarget)) == true && new FileInfo(FilePathMap.BSGBackendCustomizationResultsIndexFilePath(jobTarget)).Length > 0)
                        {
                            FileIOHelper.AppendTwoCSVFiles(FilePathMap.BSGBackendCustomizationResultsExcelReportFilePath(), FilePathMap.BSGBackendCustomizationResultsIndexFilePath(jobTarget));
                        }
                        // Append all the individual report files into one
                        if (File.Exists(FilePathMap.BSGSepResultsIndexFilePath(jobTarget)) == true && new FileInfo(FilePathMap.BSGSepResultsIndexFilePath(jobTarget)).Length > 0)
                        {
                            FileIOHelper.AppendTwoCSVFiles(FilePathMap.BSGSepResultsExcelReportFilePath(), FilePathMap.BSGSepResultsIndexFilePath(jobTarget));
                        }
                        // Append all the individual report files into one
                        if (File.Exists(FilePathMap.BSGHealthRuleResultsIndexFilePath(jobTarget)) == true && new FileInfo(FilePathMap.BSGHealthRuleResultsIndexFilePath(jobTarget)).Length > 0)
                        {
                            FileIOHelper.AppendTwoCSVFiles(FilePathMap.BSGHealthRuleResultsExcelReportFilePath(), FilePathMap.BSGHealthRuleResultsIndexFilePath(jobTarget));
                        }

                        // Append all the individual report files into one
                        if (File.Exists(FilePathMap.BSGOverheadResultsIndexFilePath(jobTarget)) == true && new FileInfo(FilePathMap.BSGOverheadResultsIndexFilePath(jobTarget)).Length > 0)
                        {
                            FileIOHelper.AppendTwoCSVFiles(FilePathMap.BSGOverheadResultsExcelReportFilePath(), FilePathMap.BSGOverheadResultsIndexFilePath(jobTarget));
                        }

                        // Append all the individual report files into one
                        if (File.Exists(FilePathMap.BSGDataCollectorResultsIndexFilePath(jobTarget)) == true && new FileInfo(FilePathMap.BSGDataCollectorResultsIndexFilePath(jobTarget)).Length > 0)
                        {
                            FileIOHelper.AppendTwoCSVFiles(FilePathMap.BSGDataCollectorResultsExcelReportFilePath(), FilePathMap.BSGDataCollectorResultsIndexFilePath(jobTarget));
                        }

                        // Append all the individual report files into one
                        if (File.Exists(FilePathMap.BSGDashboardResultsIndexFilePath(jobTarget)) == true && new FileInfo(FilePathMap.BSGDashboardResultsIndexFilePath(jobTarget)).Length > 0)
                        {
                            FileIOHelper.AppendTwoCSVFiles(FilePathMap.BSGDashboardResultsExcelReportFilePath(), FilePathMap.BSGDashboardResultsIndexFilePath(jobTarget));
                        }
                        
                        // Append all the individual report files into one
                        if (File.Exists(FilePathMap.BSGErrorsResultsIndexFilePath(jobTarget)) == true && new FileInfo(FilePathMap.BSGErrorsResultsIndexFilePath(jobTarget)).Length > 0)
                        {
                            FileIOHelper.AppendTwoCSVFiles(FilePathMap.BSGErrorsResultsExcelReportFilePath(), FilePathMap.BSGErrorsResultsIndexFilePath(jobTarget));
                        }
                        
                        #endregion
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(ex);
                        loggerConsole.Warn(ex);

                        return false;
                    }
                    finally
                    {
                        stopWatchTarget.Stop();

                        this.DisplayJobTargetEndedStatus(jobConfiguration, jobTarget, i + 1, stopWatchTarget);

                        stepTimingTarget.EndTime = DateTime.Now;
                        stepTimingTarget.Duration = stopWatchTarget.Elapsed;
                        stepTimingTarget.DurationMS = stopWatchTarget.ElapsedMilliseconds;

                        List<StepTiming> stepTimings = new List<StepTiming>(1);
                        stepTimings.Add(stepTimingTarget);
                        FileIOHelper.WriteListToCSVFile(stepTimings, new StepTimingReportMap(), FilePathMap.StepTimingReportFilePath(), true);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);

                return false;
            }
            finally
            {
                stopWatch.Stop();

                this.DisplayJobStepEndedStatus(jobConfiguration, stopWatch);

                stepTimingFunction.EndTime = DateTime.Now;
                stepTimingFunction.Duration = stopWatch.Elapsed;
                stepTimingFunction.DurationMS = stopWatch.ElapsedMilliseconds;

                List<StepTiming> stepTimings = new List<StepTiming>(1);
                stepTimings.Add(stepTimingFunction);
                FileIOHelper.WriteListToCSVFile(stepTimings, new StepTimingReportMap(), FilePathMap.StepTimingReportFilePath(), true);
            }
        }

        public override bool ShouldExecute(ProgramOptions programOptions, JobConfiguration jobConfiguration)
        {
            logger.Trace("LicensedReports.BSG={0}", programOptions.LicensedReports.BSG);
            loggerConsole.Trace("LicensedReports.BSG={0}", programOptions.LicensedReports.BSG);
            if (programOptions.LicensedReports.BSG == false)
            {
                loggerConsole.Warn("Not licensed for BSG");
                return false;
            }

            logger.Trace("Input.DetectedEntities={0}", jobConfiguration.Input.DetectedEntities);
            logger.Trace("Input.Metrics={0}", jobConfiguration.Input.Metrics);
            logger.Trace("Input.Configuration={0}", jobConfiguration.Input.Configuration);
            logger.Trace("Output.BSG={0}", jobConfiguration.Output.BSG);
            loggerConsole.Trace("Input.DetectedEntities={0}", jobConfiguration.Input.DetectedEntities);
            loggerConsole.Trace("Input.Metrics={0}", jobConfiguration.Input.Metrics);
            loggerConsole.Trace("Input.Configuration={0}", jobConfiguration.Input.Configuration);
            loggerConsole.Trace("Output.BSG={0}", jobConfiguration.Output.BSG);
            if (jobConfiguration.Input.DetectedEntities == false ||
                jobConfiguration.Input.Metrics == false ||
                jobConfiguration.Input.Configuration == false || 
                jobConfiguration.Output.BSG == false)
            {
                loggerConsole.Trace("Skipping index of BSG");
            }
            return (jobConfiguration.Input.DetectedEntities == true &&
                jobConfiguration.Input.Metrics == true &&
                jobConfiguration.Input.Configuration == true &&
                jobConfiguration.Output.BSG == true);
        }

        #region Application Naming

        /// <summary>
        /// Length of the APM Application Name
        /// </summary>
        /// <param name="jobTarget"></param>
        /// <param name="healthCheckSettingsDictionary"></param>
        /// <returns></returns>
        private HealthCheckRuleResult evaluate_APMApplication_Name_Length(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration)
        {
            logger.Trace("Evaluating {0}", hcrd);

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application"));

            if (jobTarget.Application.Length > getIntegerSetting(healthCheckSettingsDictionary, "APMApplicationNameLengthGrade2", 60))
            {
                healthCheckRuleResult.Grade = 1;
                healthCheckRuleResult.Description = String.Format("The Application Name '{0}' is very, very, very long (>'{1}' characters)", jobTarget.Application, getIntegerSetting(healthCheckSettingsDictionary, "APMApplicationNameLengthGrade2", 60));
            }
            else if (jobTarget.Application.Length > getIntegerSetting(healthCheckSettingsDictionary, "APMApplicationNameLengthGrade3", 50))
            {
                healthCheckRuleResult.Grade = 2;
                healthCheckRuleResult.Description = String.Format("The Application Name '{0}' is very, very long (>'{1}' characters)", jobTarget.Application, getIntegerSetting(healthCheckSettingsDictionary, "APMApplicationNameLengthGrade3", 50));
            }
            else if (jobTarget.Application.Length > getIntegerSetting(healthCheckSettingsDictionary, "APMApplicationNameLengthGrade4", 40))
            {
                healthCheckRuleResult.Grade = 3;
                healthCheckRuleResult.Description = String.Format("The Application Name '{0}' is very long (>'{1}' characters)", jobTarget.Application, getIntegerSetting(healthCheckSettingsDictionary, "APMApplicationNameLengthGrade2", 40));
            }
            else if (jobTarget.Application.Length > getIntegerSetting(healthCheckSettingsDictionary, "APMApplicationNameLengthGrade5", 30))
            {
                healthCheckRuleResult.Grade = 4;
                healthCheckRuleResult.Description = String.Format("The Application Name '{0}' is long (>'{1}' characters)", jobTarget.Application, getIntegerSetting(healthCheckSettingsDictionary, "APMApplicationNameLengthGrade5", 30));
            }
            else
            {
                healthCheckRuleResult.Grade = 5;
                healthCheckRuleResult.Description = String.Format("The Application Name '{0}' is within recommended length (<='{1}' characters)", jobTarget.Application, getIntegerSetting(healthCheckSettingsDictionary, "APMApplicationNameLengthGrade5", 30));
            }
            
            return healthCheckRuleResult;
        }

        /// <summary>
        /// APM Application name should contain environment designation
        /// </summary>
        /// <param name="jobTarget"></param>
        /// <param name="healthCheckSettingsDictionary"></param>
        /// <returns></returns>
        private HealthCheckRuleResult evaluate_APMApplication_Name_Environment(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration)
        {
            logger.Trace("Evaluating {0}", hcrd);

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application"));

            Regex regexQuery = new Regex(getStringSetting(healthCheckSettingsDictionary, "APMEntityNameEnvironmentRegex", "(production|prod|qa|test|tst|nonprod|perf|performance|sit|clt|dev|uat|poc|pov|demo|stage|stg)"), RegexOptions.IgnoreCase);
            Match regexMatch = regexQuery.Match(jobTarget.Application);
            if (regexMatch.Success == true && regexMatch.Groups.Count == 2)
            {
                healthCheckRuleResult.Grade = 5;
                healthCheckRuleResult.Description = String.Format("The Application Name '{0}' contains environment designation '{1}'", jobTarget.Application, regexMatch.Groups[1].Value);
            }
            else
            {
                healthCheckRuleResult.Grade = 3;
                healthCheckRuleResult.Description = String.Format("The Application Name '{0}' does not contain environment designation", jobTarget.Application);
            }

            return healthCheckRuleResult;
        }

        #endregion

        #region Application Agent Properties

        /// <summary>
        /// Built-in properties modified from their default values
        /// Any change is graded 3
        /// </summary>
        /// <param name="jobTarget"></param>
        /// <param name="healthCheckSettingsDictionary"></param>
        /// <param name="agentPropertiesList"></param>
        /// <returns></returns>
        private List<HealthCheckRuleResult> evaluate_APMApplication_Properties_BuiltIn_Modified(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<AgentConfigurationProperty> agentPropertiesList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application_Properties"));
            healthCheckRuleResult.Grade = 5;
            healthCheckRuleResult.Description = "None of the built-in Agent Properties are modified from their default values";

            if (agentPropertiesList != null && agentPropertiesList.Count > 0)
            {
                List<AgentConfigurationProperty> agentPropertiesBuiltInList = agentPropertiesList.Where(p => BUILTIN_AGENT_PROPERTIES.Contains(p.PropertyName) == true && p.IsDefault == false && p.TierName.Length == 0).ToList();
                if (agentPropertiesBuiltInList != null && agentPropertiesBuiltInList.Count > 0)
                {
                    foreach (AgentConfigurationProperty agentProperty in agentPropertiesBuiltInList)
                    {
                        HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                        healthCheckRuleResult1.Grade = 3;

                        switch (agentProperty.PropertyType)
                        {
                            case "BOOLEAN":
                                healthCheckRuleResult1.Description = String.Format("Built-in Agent Property '{0}\\{1} [{2}]' value is '{3}' and modified from its default value '{4}'", agentProperty.AgentType, agentProperty.PropertyName, agentProperty.PropertyType, agentProperty.BooleanValue, agentProperty.BooleanDefaultValue);
                                break;

                            case "INTEGER":
                                healthCheckRuleResult1.Description = String.Format("Built-in Agent Property '{0}\\{1} [{2}]' value is '{3}' and modified from its default value '{4}'", agentProperty.AgentType, agentProperty.PropertyName, agentProperty.PropertyType, agentProperty.IntegerValue, agentProperty.IntegerDefaultValue);
                                break;

                            case "STRING":
                                healthCheckRuleResult1.Description = String.Format("Built-in Agent Property '{0}\\{1} [{2}]' value is '{3}' and modified from its default value '{4}'", agentProperty.AgentType, agentProperty.PropertyName, agentProperty.PropertyType, agentProperty.StringValue, agentProperty.StringDefaultValue);
                                break;

                            default:
                                break;
                        }

                        healthCheckRuleResults.Add(healthCheckRuleResult1);
                    }
                }
            }

            if (healthCheckRuleResults.Count == 0)
            {
                healthCheckRuleResults.Add(healthCheckRuleResult);
            }

            return healthCheckRuleResults;
        }

        /// <summary>
        /// Non-built in properties added
        /// Any change is graded 3
        /// </summary>
        /// <param name="jobTarget"></param>
        /// <param name="healthCheckSettingsDictionary"></param>
        /// <param name="jobConfiguration"></param>
        /// <param name="agentPropertiesList"></param>
        /// <returns></returns>
        private List<HealthCheckRuleResult> evaluate_APMApplication_Properties_New_Added(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<AgentConfigurationProperty> agentPropertiesList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application_Properties"));
            healthCheckRuleResult.Grade = 5;
            healthCheckRuleResult.Description = "No additional non-default Agent Properties are added to application configuration";

            if (agentPropertiesList != null && agentPropertiesList.Count > 0)
            {
                List<AgentConfigurationProperty> agentPropertiesNonBuiltIntList = agentPropertiesList.Where(p => BUILTIN_AGENT_PROPERTIES.Contains(p.PropertyName) == false && p.TierName.Length == 0).ToList();
                if (agentPropertiesNonBuiltIntList != null && agentPropertiesNonBuiltIntList.Count > 0)
                {
                    foreach (AgentConfigurationProperty agentProperty in agentPropertiesNonBuiltIntList)
                    {
                        HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                        healthCheckRuleResult1.Grade = 3;

                        switch (agentProperty.PropertyType)
                        {
                            case "BOOLEAN":
                                healthCheckRuleResult1.Description = String.Format("Non-default Agent Property '{0}\\{1} [{2}]' value is '{3}' and its default value is '{4}'", agentProperty.AgentType, agentProperty.PropertyName, agentProperty.PropertyType, agentProperty.BooleanValue, agentProperty.BooleanDefaultValue);
                                break;

                            case "INTEGER":
                                healthCheckRuleResult1.Description = String.Format("Non-default Agent Property '{0}\\{1} [{2}]' value is '{3}' and its default value is '{4}'", agentProperty.AgentType, agentProperty.PropertyName, agentProperty.PropertyType, agentProperty.IntegerValue, agentProperty.IntegerDefaultValue);
                                break;

                            case "STRING":
                                healthCheckRuleResult1.Description = String.Format("Non-default Agent Property '{0}\\{1} [{2}]' value is '{3}' and its default value is '{4}'", agentProperty.AgentType, agentProperty.PropertyName, agentProperty.PropertyType, agentProperty.StringValue, agentProperty.StringDefaultValue);
                                break;

                            default:
                                break;
                        }

                        healthCheckRuleResults.Add(healthCheckRuleResult1);
                    }
                }
            }

            if (healthCheckRuleResults.Count == 0)
            {            
                healthCheckRuleResults.Add(healthCheckRuleResult);
            }

            return healthCheckRuleResults;
        }

        /// <summary>
        /// Well-known properties - analytics-dynamic-service-enabled
        /// if it is not set at all , grade 5
        /// if it is set, grade 5
        /// if it is set for some but not others, grade 1
        /// </summary>
        /// <param name="jobTarget"></param>
        /// <param name="healthCheckSettingsDictionary"></param>
        /// <param name="jobConfiguration"></param>
        /// <param name="agentPropertiesList"></param>
        /// <returns></returns>
        private List<HealthCheckRuleResult> evaluate_APMApplication_Properties_WellKnown_analytics_dynamic_service_enabled(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<AgentConfigurationProperty> agentPropertiesList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application_Properties"));
            healthCheckRuleResult.Grade = 5;
            healthCheckRuleResult.Description = "BIQ/Analytics is not enabled via 'analytics-dynamic-service-enabled' Agent Property for any agent types";

            if (agentPropertiesList != null && agentPropertiesList.Count > 0)
            {
                List<AgentConfigurationProperty> agentPropertiesAnalyticsEnabledList = agentPropertiesList.Where(p => p.PropertyName == "analytics-dynamic-service-enabled" && p.BooleanValue == true && p.TierName.Length == 0).ToList();
                if (agentPropertiesAnalyticsEnabledList != null && agentPropertiesAnalyticsEnabledList.Count > 0)
                {
                    foreach (AgentConfigurationProperty agentProperty in agentPropertiesAnalyticsEnabledList)
                    {
                        HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                        healthCheckRuleResult1.Grade = 5;
                        healthCheckRuleResult1.Description = String.Format("BIQ/Analytics is enabled via '{0}\\{1} [{2}]' Agent Property set to '{3}' for agent type '{4}'", agentProperty.AgentType, agentProperty.PropertyName, agentProperty.PropertyType, agentProperty.BooleanValue, agentProperty.AgentType);
                 
                        healthCheckRuleResults.Add(healthCheckRuleResult1);
                    }
                }

                List<AgentConfigurationProperty> agentPropertiesAnalyticsDisabledList = agentPropertiesList.Where(p => p.PropertyName == "analytics-dynamic-service-enabled" && p.BooleanValue == false && p.TierName.Length == 0).ToList();
                if (agentPropertiesAnalyticsDisabledList != null && agentPropertiesAnalyticsDisabledList.Count > 0)
                {
                    foreach (AgentConfigurationProperty agentProperty in agentPropertiesAnalyticsDisabledList)
                    {
                        HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                        healthCheckRuleResult1.Grade = 1;
                        healthCheckRuleResult1.Description = String.Format("BIQ/Analytics is disabled via '{0}\\{1} [{2}]' Agent Property set to '{3}' for agent type '{4}'", agentProperty.AgentType, agentProperty.PropertyName, agentProperty.PropertyType, agentProperty.BooleanValue, agentProperty.AgentType);
                    
                        healthCheckRuleResults.Add(healthCheckRuleResult1);
                    }
                }
            }
            
            if (healthCheckRuleResults.Count == 0)
            {
                healthCheckRuleResults.Add(healthCheckRuleResult);
            }

            return healthCheckRuleResults;
        }

        /// <summary>
        /// Well-known properties - find-entry-points
        /// If it is set at all, grade 2
        /// </summary>
        /// <param name="jobTarget"></param>
        /// <param name="healthCheckSettingsDictionary"></param>
        /// <param name="jobConfiguration"></param>
        /// <param name="agentPropertiesList"></param>
        /// <returns></returns>
        private List<HealthCheckRuleResult> evaluate_APMApplication_Properties_WellKnown_find_entry_points(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<AgentConfigurationProperty> agentPropertiesList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application_Properties"));
            healthCheckRuleResult.Grade = 5;
            healthCheckRuleResult.Description = "Business Transaction discovery stack logging is disabled via 'find-entry-points' Agent Property for all agent types";

            if (agentPropertiesList != null && agentPropertiesList.Count > 0)
            {
                List<AgentConfigurationProperty> agentPropertiesFindEntryPointsDisabledList = agentPropertiesList.Where(p => p.PropertyName == "find-entry-points" && p.BooleanValue == false && p.TierName.Length == 0).ToList();
                if (agentPropertiesFindEntryPointsDisabledList != null && agentPropertiesFindEntryPointsDisabledList.Count > 0)
                {
                    foreach (AgentConfigurationProperty agentProperty in agentPropertiesFindEntryPointsDisabledList)
                    {
                        HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                        healthCheckRuleResult1.Grade = 5;
                        healthCheckRuleResult1.Description = String.Format("Business Transaction discovery stack logging is disabled via '{0}\\{1} [{2}]' Agent Property set to '{3}' for agent type '{4}'", agentProperty.AgentType, agentProperty.PropertyName, agentProperty.PropertyType, agentProperty.BooleanValue, agentProperty.AgentType);
                 
                        healthCheckRuleResults.Add(healthCheckRuleResult1);
                    }
                }

                List<AgentConfigurationProperty> agentPropertiesFindEntryPointsEnabledList = agentPropertiesList.Where(p => p.PropertyName == "find-entry-points" && p.BooleanValue == true && p.TierName.Length == 0).ToList();
                if (agentPropertiesFindEntryPointsEnabledList != null && agentPropertiesFindEntryPointsEnabledList.Count > 0)
                {
                    foreach (AgentConfigurationProperty agentProperty in agentPropertiesFindEntryPointsEnabledList)
                    {
                        HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                        healthCheckRuleResult1.Grade = 2;
                        healthCheckRuleResult1.Description = String.Format("Business Transaction discovery stack logging is enabled via '{0}\\{1} [{2}]' Agent Property set to '{3}' for agent type '{4}'", agentProperty.AgentType, agentProperty.PropertyName, agentProperty.PropertyType, agentProperty.BooleanValue, agentProperty.AgentType);
                    
                        healthCheckRuleResults.Add(healthCheckRuleResult1);
                    }
                }
            }

            if (healthCheckRuleResults.Count == 0)
            {                
                healthCheckRuleResults.Add(healthCheckRuleResult);

            }
            return healthCheckRuleResults;
        }

        /// <summary>
        /// Well-known properties - use-old-servlet-split-for-get-parm-value-rule
        /// You set it, you get graded lowest
        /// </summary>
        /// <param name="jobTarget"></param>
        /// <param name="healthCheckSettingsDictionary"></param>
        /// <param name="jobConfiguration"></param>
        /// <param name="agentPropertiesList"></param>
        /// <returns></returns>
        private List<HealthCheckRuleResult> evaluate_APMApplication_Properties_WellKnown_use_old_servlet_split_for_get_parm_value_rule(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<AgentConfigurationProperty> agentPropertiesList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application_Properties"));
            healthCheckRuleResult.Grade = 5;
            healthCheckRuleResult.Description = "Dangerous Servlet parameter parsing option is disabled via 'use-old-servlet-split-for-get-parm-value-rule' Agent Property for all agent types";

            if (agentPropertiesList != null && agentPropertiesList.Count > 0)
            {
                List<AgentConfigurationProperty> agentPropertiesEvilOldServletSplitEnabledList = agentPropertiesList.Where(p => p.PropertyName == "use-old-servlet-split-for-get-parm-value-rule" && p.BooleanValue == true && p.TierName.Length == 0).ToList();
                if (agentPropertiesEvilOldServletSplitEnabledList != null && agentPropertiesEvilOldServletSplitEnabledList.Count > 0)
                {
                    foreach (AgentConfigurationProperty agentProperty in agentPropertiesEvilOldServletSplitEnabledList)
                    {
                        HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                        healthCheckRuleResult1.Grade = 1;
                        healthCheckRuleResult1.Description = String.Format("Dangerous Servlet parameter parsing option is enabled via '{0}\\{1} [{2}]' Agent Property set to '{3}' for agent type '{4}'", agentProperty.AgentType, agentProperty.PropertyName, agentProperty.PropertyType, agentProperty.BooleanValue, agentProperty.AgentType);

                        healthCheckRuleResults.Add(healthCheckRuleResult1);
                    }
                }
            }

            if (healthCheckRuleResults.Count == 0)
            {
                healthCheckRuleResults.Add(healthCheckRuleResult);
            }

            return healthCheckRuleResults;
        }

        /// <summary>
        /// Well-known properties - max-business-transactions
        /// Set higher than standard - grade 2
        /// Set lower than standard, grade 3
        /// </summary>
        /// <param name="jobTarget"></param>
        /// <param name="healthCheckSettingsDictionary"></param>
        /// <param name="jobConfiguration"></param>
        /// <param name="agentPropertiesList"></param>
        /// <returns></returns>
        private List<HealthCheckRuleResult> evaluate_APMApplication_Properties_WellKnown_use_max_business_transactions(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<AgentConfigurationProperty> agentPropertiesList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application_Properties"));
            healthCheckRuleResult.Grade = 5;
            healthCheckRuleResult.Description = "Maximum Business Transaction slot registration are at default value via 'max-business-transactions' Agent Property for all agent types";

            if (agentPropertiesList != null && agentPropertiesList.Count > 0)
            {
                List<AgentConfigurationProperty> agentPropertiesMaxBTGreaterThanDefault = agentPropertiesList.Where(p => p.PropertyName == "max-business-transactions" && p.IntegerValue > 50 && p.TierName.Length == 0).ToList();
                if (agentPropertiesMaxBTGreaterThanDefault != null && agentPropertiesMaxBTGreaterThanDefault.Count > 0)
                {
                    foreach (AgentConfigurationProperty agentProperty in agentPropertiesMaxBTGreaterThanDefault)
                    {
                        HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                        healthCheckRuleResult1.Grade = 2;
                        healthCheckRuleResult1.Description = String.Format("Higher than standard Business Transaction slot registration value is set via '{0}\\{1} [{2}]' Agent Property set to '{3}' for agent type '{4}'", agentProperty.AgentType, agentProperty.PropertyName, agentProperty.PropertyType, agentProperty.IntegerValue, agentProperty.AgentType);

                        healthCheckRuleResults.Add(healthCheckRuleResult1);
                    }
                }

                List<AgentConfigurationProperty> agentPropertiesMaxBTLessThanDefault = agentPropertiesList.Where(p => p.PropertyName == "max-business-transactions" && p.IntegerValue < 50 && p.TierName.Length == 0).ToList();
                if (agentPropertiesMaxBTLessThanDefault != null && agentPropertiesMaxBTLessThanDefault.Count > 0)
                {
                    foreach (AgentConfigurationProperty agentProperty in agentPropertiesMaxBTLessThanDefault)
                    {
                        HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                        healthCheckRuleResult1.Grade = 3;
                        healthCheckRuleResult1.Description = String.Format("Lower than standard Business Transaction slot registration value is set via '{0}\\{1} [{2}]' Agent Property set to '{3}' for agent type '{4}'", agentProperty.AgentType, agentProperty.PropertyName, agentProperty.PropertyType, agentProperty.IntegerValue, agentProperty.AgentType);

                        healthCheckRuleResults.Add(healthCheckRuleResult1);
                    }
                }
            }

            if (healthCheckRuleResults.Count == 0)
            {
                healthCheckRuleResults.Add(healthCheckRuleResult);
            }

            return healthCheckRuleResults;
        }

        #endregion

        #region Tier Agent Property evaluations

        /// <summary>
        /// Tier override has properties?
        /// Any change is graded 3
        /// </summary>
        /// <param name="jobTarget"></param>
        /// <param name="healthCheckSettingsDictionary"></param>
        /// <param name="agentPropertiesList"></param>
        /// <returns></returns>
        private List<HealthCheckRuleResult> evaluate_APMApplication_Tier_Properties_Overriden(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<AgentConfigurationProperty> agentPropertiesList,
            List<APMTier> tiersList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "TODO"));

            if (agentPropertiesList != null && agentPropertiesList.Count > 0)
            {
                List<AgentConfigurationProperty> agentPropertiesTierOverridePropertiesList = agentPropertiesList.Where(p => p.TierName.Length > 0).ToList();
                if (agentPropertiesTierOverridePropertiesList != null && agentPropertiesTierOverridePropertiesList.Count > 0)
                {
                    var agentPropertiesTierOverridePropertiesListUniqueTiers = agentPropertiesTierOverridePropertiesList.GroupBy(p => p.TierName);
                    foreach (var uniqueTier in agentPropertiesTierOverridePropertiesListUniqueTiers)
                    {
                        List<AgentConfigurationProperty> agentPropertiesInTier = uniqueTier.ToList();
                        AgentConfigurationProperty agentProperty = agentPropertiesInTier[0];

                        HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();

                        healthCheckRuleResult1.EntityType = "APMTier";
                        healthCheckRuleResult1.EntityName = agentProperty.TierName;
                        if (tiersList != null)
                        {
                            APMTier tier = tiersList.Where(t => t.TierName == agentProperty.TierName).FirstOrDefault();
                            if (tier != null)
                            {
                                healthCheckRuleResult1.EntityID = tier.TierID;
                            }
                            else
                            {
                                healthCheckRuleResult1.EntityID = 0;
                            }
                        }
                        
                        healthCheckRuleResult1.Grade = 3;
                        healthCheckRuleResult1.Description = String.Format("Tier '{0} [{1}]' has Agent Property override flag on, overriding '{2}' properties", agentProperty.TierName, agentProperty.AgentType, agentPropertiesInTier.Count);

                        healthCheckRuleResults.Add(healthCheckRuleResult1);
                    }
                }
            }

            if (healthCheckRuleResults.Count == 0)
            {
                healthCheckRuleResult.Grade = 5;
                healthCheckRuleResult.Description = "None of the Application Tiers have Agent Properties override";
                healthCheckRuleResults.Add(healthCheckRuleResult);
            }
            else
            {
                healthCheckRuleResult.Grade = 2;
                healthCheckRuleResult.Description = "Some Application Tiers have Agent Properties override";
                healthCheckRuleResults.Add(healthCheckRuleResult);
            }

            return healthCheckRuleResults;
        }

        /// <summary>
        /// Tier override has things changed from defaults
        /// Any change is graded 3
        /// </summary>
        /// <param name="jobTarget"></param>
        /// <param name="healthCheckSettingsDictionary"></param>
        /// <param name="agentPropertiesList"></param>
        /// <returns></returns>
        private List<HealthCheckRuleResult> evaluate_APMApplication_Tier_Properties_Overriden_BuiltIn_Modified(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<AgentConfigurationProperty> agentPropertiesList,
            List<APMTier> tiersList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMTier", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "TODO"));
            healthCheckRuleResult.Grade = 5;
            healthCheckRuleResult.Description = "None of the Application Tiers with Agent Properties override on have modified built-in properties";

            if (agentPropertiesList != null && agentPropertiesList.Count > 0)
            {
                List<AgentConfigurationProperty> agentPropertiesBuiltInList = agentPropertiesList.Where(p => BUILTIN_AGENT_PROPERTIES.Contains(p.PropertyName) == true && p.IsDefault == false && p.TierName.Length > 0).ToList();
                if (agentPropertiesBuiltInList != null && agentPropertiesBuiltInList.Count > 0)
                {
                    foreach (AgentConfigurationProperty agentProperty in agentPropertiesBuiltInList)
                    {
                        HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                        healthCheckRuleResult1.Grade = 3;

                        healthCheckRuleResult1.EntityName = agentProperty.TierName;
                        if (tiersList != null)
                        {
                            APMTier tier = tiersList.Where(t => t.TierName == agentProperty.TierName).FirstOrDefault();
                            if (tier != null)
                            {
                                healthCheckRuleResult1.EntityID = tier.TierID;
                            }
                            else
                            {
                                healthCheckRuleResult1.EntityID = 0;
                            }
                        }

                        switch (agentProperty.PropertyType)
                        {
                            case "BOOLEAN":
                                healthCheckRuleResult1.Description = String.Format("Built-in Agent Property '{0}\\{1} [{2}]' value is '{3}' and modified from its default value '{4}'", agentProperty.AgentType, agentProperty.PropertyName, agentProperty.PropertyType, agentProperty.BooleanValue, agentProperty.BooleanDefaultValue);
                                break;

                            case "INTEGER":
                                healthCheckRuleResult1.Description = String.Format("Built-in Agent Property '{0}\\{1} [{2}]' value is '{3}' and modified from its default value '{4}'", agentProperty.AgentType, agentProperty.PropertyName, agentProperty.PropertyType, agentProperty.IntegerValue, agentProperty.IntegerDefaultValue);
                                break;

                            case "STRING":
                                healthCheckRuleResult1.Description = String.Format("Built-in Agent Property '{0}\\{1} [{2}]' value is '{3}' and modified from its default value '{4}'", agentProperty.AgentType, agentProperty.PropertyName, agentProperty.PropertyType, agentProperty.StringValue, agentProperty.StringDefaultValue);
                                break;

                            default:
                                break;
                        }

                        healthCheckRuleResults.Add(healthCheckRuleResult1);
                    }
                }
            }

            if (healthCheckRuleResults.Count == 0)
            {
                healthCheckRuleResults.Add(healthCheckRuleResult);
            }

            return healthCheckRuleResults;
        }

        /// <summary>
        /// Non-built in properties added to the Tier override
        /// Any change is graded 3
        /// </summary>
        /// <param name="jobTarget"></param>
        /// <param name="healthCheckSettingsDictionary"></param>
        /// <param name="jobConfiguration"></param>
        /// <param name="agentPropertiesList"></param>
        /// <param name="tiersList"></param>
        /// <returns></returns>
        private List<HealthCheckRuleResult> evaluate_APMApplication_Tier_Properties_Overriden_New_Added(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<AgentConfigurationProperty> agentPropertiesList,
            List<APMTier> tiersList)

        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMTier", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application_Properties"));
            healthCheckRuleResult.Grade = 5;
            healthCheckRuleResult.Description = "None of the Application Tiers with Agent Properties override on have additional non-default Agent Properties added to tier configuration";

            if (agentPropertiesList != null && agentPropertiesList.Count > 0)
            {
                List<AgentConfigurationProperty> agentPropertiesNonBuiltIntList = agentPropertiesList.Where(p => BUILTIN_AGENT_PROPERTIES.Contains(p.PropertyName) == false && p.TierName.Length > 0).ToList();
                if (agentPropertiesNonBuiltIntList != null && agentPropertiesNonBuiltIntList.Count > 0)
                {
                    foreach (AgentConfigurationProperty agentProperty in agentPropertiesNonBuiltIntList)
                    {
                        HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                        healthCheckRuleResult1.Grade = 2;

                        healthCheckRuleResult1.EntityName = agentProperty.TierName;
                        if (tiersList != null)
                        {
                            APMTier tier = tiersList.Where(t => t.TierName == agentProperty.TierName).FirstOrDefault();
                            if (tier != null)
                            {
                                healthCheckRuleResult1.EntityID = tier.TierID;
                            }
                            else
                            {
                                healthCheckRuleResult1.EntityID = 0;
                            }
                        }

                        switch (agentProperty.PropertyType)
                        {
                            case "BOOLEAN":
                                healthCheckRuleResult1.Description = String.Format("Non-default Agent Property '{0}\\{1} [{2}]' value is '{3}' and its default value is '{4}'", agentProperty.AgentType, agentProperty.PropertyName, agentProperty.PropertyType, agentProperty.BooleanValue, agentProperty.BooleanDefaultValue);
                                break;

                            case "INTEGER":
                                healthCheckRuleResult1.Description = String.Format("Non-default Agent Property '{0}\\{1} [{2}]' value is '{3}' and its default value is '{4}'", agentProperty.AgentType, agentProperty.PropertyName, agentProperty.PropertyType, agentProperty.IntegerValue, agentProperty.IntegerDefaultValue);
                                break;

                            case "STRING":
                                healthCheckRuleResult1.Description = String.Format("Non-default Agent Property '{0}\\{1} [{2}]' value is '{3}' and its default value is '{4}'", agentProperty.AgentType, agentProperty.PropertyName, agentProperty.PropertyType, agentProperty.StringValue, agentProperty.StringDefaultValue);
                                break;

                            default:
                                break;
                        }

                        healthCheckRuleResults.Add(healthCheckRuleResult1);
                    }
                }
            }

            if (healthCheckRuleResults.Count == 0)
            {
                healthCheckRuleResults.Add(healthCheckRuleResult);
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMTier_AgentVersion_Uniformity(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMTier> tiersList,
            List<APMNode> nodesList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMTier", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (tiersList != null && nodesList != null)
            {
                foreach (APMTier tier in tiersList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();

                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("tier", tier.TierName, tier.TierID)));

                    healthCheckRuleResult1.EntityName = tier.TierName;

                    List<APMNode> nodesInTierList = nodesList.Where(n => n.TierID == tier.TierID && n.AgentPresent == true).ToList();

                    if (nodesInTierList != null && nodesInTierList.Count > 0)
                    {
                        var nodesInTierVersionsGroup = nodesInTierList.GroupBy(n => n.AgentVersion);

                        if (nodesInTierVersionsGroup.Count() == 1)
                        {
                            healthCheckRuleResult1.Grade = 5;
                            healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' has consistent version '{2}'", tier.TierName, tier.AgentType, nodesInTierList[0].AgentVersion);
                        }
                        else if (nodesInTierVersionsGroup.Count() == 2)
                        {
                            healthCheckRuleResult1.Grade = 3;
                            healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' has '{2}' different versions ('{3}')", tier.TierName, tier.AgentType, nodesInTierVersionsGroup.Count(), String.Join(", ", nodesInTierVersionsGroup.Select(n => n.Key).ToArray()));
                        }
                        else 
                        {
                            healthCheckRuleResult1.Grade = 1;
                            healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' has '{2}' different versions ('{3}')", tier.TierName, tier.AgentType, nodesInTierVersionsGroup.Count(), String.Join(", ", nodesInTierVersionsGroup.Select(n => n.Key).ToArray()));
                        }

                        healthCheckRuleResults.Add(healthCheckRuleResult1);
                    }
                }
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMTier_MachineAgentVersion_Uniformity(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMTier> tiersList,
            List<APMNode> nodesList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMTier", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (tiersList != null && nodesList != null)
            {
                foreach (APMTier tier in tiersList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();

                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("tier", tier.TierName, tier.TierID)));

                    healthCheckRuleResult1.EntityName = tier.TierName;

                    List<APMNode> nodesInTierList = nodesList.Where(n => n.TierID == tier.TierID && n.MachineAgentPresent == true).ToList();

                    if (nodesInTierList != null && nodesInTierList.Count > 0)
                    {
                        var nodesInTierVersionsGroup = nodesInTierList.GroupBy(n => n.MachineAgentVersion);

                        if (nodesInTierVersionsGroup.Count() == 1)
                        {
                            healthCheckRuleResult1.Grade = 5;
                            healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' has consistent version '{2}'", tier.TierName, tier.AgentType, nodesInTierList[0].MachineAgentVersion);
                        }
                        else if (nodesInTierVersionsGroup.Count() == 2)
                        {
                            healthCheckRuleResult1.Grade = 3;
                            healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' has '{2}' different versions ('{3}')", tier.TierName, tier.AgentType, nodesInTierVersionsGroup.Count(), String.Join(", ", nodesInTierVersionsGroup.Select(n => n.Key).ToArray()));
                        }
                        else
                        {
                            healthCheckRuleResult1.Grade = 1;
                            healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' has '{2}' different versions ('{3}')", tier.TierName, tier.AgentType, nodesInTierVersionsGroup.Count(), String.Join(", ", nodesInTierVersionsGroup.Select(n => n.Key).ToArray()));
                        }

                        healthCheckRuleResults.Add(healthCheckRuleResult1);
                    }
                }
            }

            return healthCheckRuleResults;
        }

        #endregion

        #region Tier Numbers, Names, Activities

        private HealthCheckRuleResult evaluate_APMTier_List_Of_Entities(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMTier> tiersList)
        {
            logger.Trace("Evaluating {0}", hcrd);
        
            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Tiers"));

            if (tiersList != null)
            {
                if (tiersList.Count == 0)
                {
                    healthCheckRuleResult.Grade = 1;
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has 0 Tiers", jobTarget.Application);
                }
                else if (tiersList.Count >= getIntegerSetting(healthCheckSettingsDictionary, "APMTierCountGrade2", 100))
                {
                    healthCheckRuleResult.Grade = 2;
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has too many Tiers ('{1}'>'{2}')", jobTarget.Application, tiersList.Count, getIntegerSetting(healthCheckSettingsDictionary, "APMTierCountGrade2", 50));
                }
                else if (tiersList.Count >= getIntegerSetting(healthCheckSettingsDictionary, "APMTierCountGrade3", 50))
                {
                    healthCheckRuleResult.Grade = 3;
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has almost too high number of Tiers ('{1}'>'{2}')", jobTarget.Application, tiersList.Count, getIntegerSetting(healthCheckSettingsDictionary, "APMTierCountGrade3", 50));
                }
                else if (tiersList.Count >= getIntegerSetting(healthCheckSettingsDictionary, "APMTierCountGrade4", 40))
                {
                    healthCheckRuleResult.Grade = 4;
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has high number of Tiers ('{1}'>'{2}')", jobTarget.Application, tiersList.Count, getIntegerSetting(healthCheckSettingsDictionary, "APMTierCountGrade4", 40));
                }
                else
                {
                    healthCheckRuleResult.Grade = 5;
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has a fine number of Tiers ('{1}')", jobTarget.Application, tiersList.Count);
                }
            }
            else
            {
                healthCheckRuleResult.Grade = 1;
                healthCheckRuleResult.Description = String.Format("The Application '{0}' has no Tiers to evaluate", jobTarget.Application);
            }

            return healthCheckRuleResult;
        }

        private List<HealthCheckRuleResult> evaluate_APMTier_Name_Length(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMTier> tiersList)
        {
            logger.Trace("Evaluating {0}", hcrd);
        
            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMTier", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (tiersList != null)
            {
                foreach (APMTier tier in tiersList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.Grade = 2;

                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("tier", tier.TierName, tier.TierID)));

                    healthCheckRuleResult1.EntityName = tier.TierName;
                    if (tier.TierName.Length > getIntegerSetting(healthCheckSettingsDictionary, "APMTierNameLengthGrade2", 60))
                    {
                        healthCheckRuleResult1.Grade = 1;
                        healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' is very, very, very long (>'{1}' characters)", tier.TierName, tier.AgentType, getIntegerSetting(healthCheckSettingsDictionary, "APMTierNameLengthGrade2", 60));
                    }
                    else if (tier.TierName.Length > getIntegerSetting(healthCheckSettingsDictionary, "APMTierNameLengthGrade3", 50))
                    {
                        healthCheckRuleResult1.Grade = 2;
                        healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' is very, very long (>'{1}' characters)", tier.TierName, tier.AgentType, getIntegerSetting(healthCheckSettingsDictionary, "APMTierNameLengthGrade3", 50));
                    }
                    else if (tier.TierName.Length > getIntegerSetting(healthCheckSettingsDictionary, "APMTierNameLengthGrade4", 40))
                    {
                        healthCheckRuleResult1.Grade = 3;
                        healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' is very long (>'{1}' characters)", tier.TierName, tier.AgentType, getIntegerSetting(healthCheckSettingsDictionary, "APMTierNameLengthGrade2", 40));
                    }
                    else if (tier.TierName.Length > getIntegerSetting(healthCheckSettingsDictionary, "APMTierNameLengthGrade5", 30))
                    {
                        healthCheckRuleResult1.Grade = 4;
                        healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' is long (>'{1}' characters)", tier.TierName, tier.AgentType, getIntegerSetting(healthCheckSettingsDictionary, "APMTierNameLengthGrade5", 30));
                    }
                    else
                    {
                        healthCheckRuleResult1.Grade = 5;
                        healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' is within recommended length (<='{1}' characters)", tier.TierName, getIntegerSetting(healthCheckSettingsDictionary, "APMTierNameLengthGrade5", 30));
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMTier_Name_SpecialCharacters(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMTier> tiersList)
        {
            logger.Trace("Evaluating {0}", hcrd);
        
            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMTier", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (tiersList != null)
            {
                foreach (APMTier tier in tiersList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("tier", tier.TierName, tier.TierID)));

                    healthCheckRuleResult1.EntityName = tier.TierName;
                    healthCheckRuleResult1.Grade = 5;
                    healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' has no warning characters in its name", tier.TierName, tier.AgentType);

                    bool hasWarningCharacters = false;
                    foreach (var c in getStringSetting(healthCheckSettingsDictionary, "APMTierNameWarningCharacters", @"` |\/?&").ToCharArray())
                    {
                        if (tier.TierName.Contains(c) == true)
                        {
                            hasWarningCharacters = true;

                            HealthCheckRuleResult healthCheckRuleResult2 = healthCheckRuleResult1.Clone();
                            healthCheckRuleResult2.Grade = 2;

                            healthCheckRuleResult2.Description = String.Format("The Tier '{0} [{1}]' contains warning character '{2}' in its name", tier.TierName, tier.AgentType, c);

                            healthCheckRuleResults.Add(healthCheckRuleResult2);
                        }
                    }
                    if (hasWarningCharacters == false)
                    { 
                        healthCheckRuleResults.Add(healthCheckRuleResult1);
                    }
                }
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMTier_Name_Environment(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMTier> tiersList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMTier", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (tiersList != null)
            {
                foreach (APMTier tier in tiersList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("tier", tier.TierName, tier.TierID)));

                    healthCheckRuleResult1.EntityName = tier.TierName;

                    Regex regexQuery = new Regex(getStringSetting(healthCheckSettingsDictionary, "APMEntityNameEnvironmentRegex", "(production|prod|qa|test|tst|nonprod|perf|performance|sit|clt|dev|uat|poc|pov|demo|stage|stg)"), RegexOptions.IgnoreCase);
                    Match regexMatch = regexQuery.Match(tier.TierName);
                    if (regexMatch.Success == true && regexMatch.Groups.Count == 2)
                    {
                        healthCheckRuleResult1.Grade = 2;
                        healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' has '{2}' environment designation", tier.TierName, tier.AgentType, regexMatch.Groups[1].Value);
                    }
                    else
                    {
                        healthCheckRuleResult1.Grade = 5;
                        healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' has no environment designation", tier.TierName, tier.AgentType);
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMTier_Availability_APM(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMTier> tiersMetricsList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMTier", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (tiersMetricsList != null)
            {
                foreach (APMTier tier in tiersMetricsList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.Grade = 5;

                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("tier", tier.TierName, tier.TierID)));

                    healthCheckRuleResult1.EntityName = tier.TierName;

                    if (tier.NumNodes == 0)
                    {
                        healthCheckRuleResult1.Grade = 1;
                        healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' has 0 APM Agents and therefore no availability", tier.TierName, tier.AgentType);
                    }
                    else
                    {
                        decimal percentageAvailability = Math.Round((decimal)(tier.AvailAgent / tier.NumNodes * 100), 0);

                        healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' has '{2}'% APM Agent availability ('{3}' available / '{4}' total)", tier.TierName, tier.AgentType, percentageAvailability, tier.AvailAgent, tier.NumNodes);
                        if (percentageAvailability == 0)
                        {
                            healthCheckRuleResult1.Grade = 1;
                        }
                        else if (percentageAvailability < 20)
                        {
                            healthCheckRuleResult1.Grade = 2;
                        }
                        else if (percentageAvailability < 50)
                        {
                            healthCheckRuleResult1.Grade = 3;
                        }
                        else if (percentageAvailability < 70)
                        {
                            healthCheckRuleResult1.Grade = 4;
                        }
                        else
                        {
                            healthCheckRuleResult1.Grade = 5;
                        }
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMTier_Availability_Machine(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMTier> tiersMetricsList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMTier", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (tiersMetricsList != null)
            {
                foreach (APMTier tier in tiersMetricsList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.Grade = 5;

                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("tier", tier.TierName, tier.TierID)));

                    healthCheckRuleResult1.EntityName = tier.TierName;

                    if (tier.NumNodes == 0)
                    {
                        healthCheckRuleResult1.Grade = 1;
                        healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' has 0 Machine Agents and therefore no availability", tier.TierName, tier.AgentType);
                    }
                    else
                    {
                        decimal percentageAvailability = Math.Round((decimal)(tier.AvailMachine / tier.NumNodes * 100), 0);

                        healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' has '{2}'% Machine Agent availability ('{3}' available / '{4}' total)", tier.TierName, tier.AgentType, percentageAvailability, tier.AvailMachine, tier.NumNodes);
                        if (percentageAvailability == 0)
                        {
                            healthCheckRuleResult1.Grade = 1;
                        }
                        else if (percentageAvailability < 20)
                        {
                            healthCheckRuleResult1.Grade = 2;
                        }
                        else if (percentageAvailability < 50)
                        {
                            healthCheckRuleResult1.Grade = 3;
                        }
                        else if (percentageAvailability < 70)
                        {
                            healthCheckRuleResult1.Grade = 4;
                        }
                        else
                        {
                            healthCheckRuleResult1.Grade = 5;
                        }
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMTier_Activity(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMTier> tiersMetricsList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMTier", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (tiersMetricsList != null)
            {
                foreach (APMTier tier in tiersMetricsList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.Grade = 5;

                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("tier", tier.TierName, tier.TierID)));

                    healthCheckRuleResult1.EntityName = tier.TierName;

                    if (tier.HasActivity == true)
                    {
                        healthCheckRuleResult1.Grade = 5;
                        healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' has activity", tier.TierName, tier.AgentType);
                    }
                    else
                    {
                        healthCheckRuleResult1.Grade = 2;
                        healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' has no activity", tier.TierName, tier.AgentType);
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMTier_Error_Rate(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMTier> tiersMetricsList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMTier", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (tiersMetricsList != null)
            {
                foreach (APMTier tier in tiersMetricsList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.Grade = 5;

                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("tier", tier.TierName, tier.TierID)));

                    healthCheckRuleResult1.EntityName = tier.TierName;

                    healthCheckRuleResult1.Description = String.Format("The Tier '{0} [{1}]' has '{2}'% Error rate", tier.TierName, tier.AgentType, tier.ErrorsPercentage);

                    if (tier.ErrorsPercentage <= getIntegerSetting(healthCheckSettingsDictionary, "APMTierErrorRateGrade5", 20))
                    {
                        healthCheckRuleResult1.Grade = 5;
                    }
                    else if (tier.ErrorsPercentage <= getIntegerSetting(healthCheckSettingsDictionary, "APMTierErrorRateGrade4", 40))
                    {
                        healthCheckRuleResult1.Grade = 4;
                    }
                    else if (tier.ErrorsPercentage <= getIntegerSetting(healthCheckSettingsDictionary, "APMTierErrorRateGrade4", 60))
                    {
                        healthCheckRuleResult1.Grade = 3;
                    }
                    else if (tier.ErrorsPercentage <= getIntegerSetting(healthCheckSettingsDictionary, "APMTierErrorRateGrade2", 80))
                    {
                        healthCheckRuleResult1.Grade = 2;
                    }
                    else
                    {
                        healthCheckRuleResult1.Grade = 1;
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        #endregion

        #region Business Transactions and Origins

        private HealthCheckRuleResult evaluate_APMBusinessTransaction_Number_Of_Entities(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMBusinessTransaction> businessTransactionsList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Business_Transactions"));

            if (businessTransactionsList != null)
            {
                int numRealBTs = businessTransactionsList.Where(b => b.BTType != "OVERFLOW").Count();

                if (numRealBTs == 0)
                {
                    healthCheckRuleResult.Grade = 1;
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has 0 Business Transactions", jobTarget.Application);
                }
                else if (numRealBTs <= getIntegerSetting(healthCheckSettingsDictionary, "APMBTCountGrade5", 200))
                {
                    healthCheckRuleResult.Grade = 5;
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has a normal number of Business Transactions ('{1}'<'{2}')", jobTarget.Application, numRealBTs, getIntegerSetting(healthCheckSettingsDictionary, "APMBTCountGrade5", 200));
                }
                else if (numRealBTs <= getIntegerSetting(healthCheckSettingsDictionary, "APMBTCountGrade4", 300))
                {
                    healthCheckRuleResult.Grade = 4;
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has a wee bit too many of Business Transactions ('{1}'<'{2}')", jobTarget.Application, numRealBTs, getIntegerSetting(healthCheckSettingsDictionary, "APMBTCountGrade4", 300));
                }
                else if (numRealBTs <= getIntegerSetting(healthCheckSettingsDictionary, "APMBTCountGrade3", 500))
                {
                    healthCheckRuleResult.Grade = 3;
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has kind of a lot of Business Transactions ('{1}'<'{2}')", jobTarget.Application, numRealBTs, getIntegerSetting(healthCheckSettingsDictionary, "APMBTCountGrade3", 500));
                }
                else
                {
                    healthCheckRuleResult.Grade = 2;
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has way too many of Business Transactions ('{1}'>'{2}')", jobTarget.Application, numRealBTs, getIntegerSetting(healthCheckSettingsDictionary, "APMBTCountGrade3", 500));
                }
            }
            else
            {
                healthCheckRuleResult.Grade = 1;
                healthCheckRuleResult.Description = String.Format("The Application '{0}' has 0 Business Transactions to evaluate", jobTarget.Application);
            }

            return healthCheckRuleResult;
        }

        private HealthCheckRuleResult evaluate_APMBusinessTransaction_Automatic_Or_Explicit_Ratio(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMBusinessTransaction> businessTransactionsList)
        {
            logger.Trace("Evaluating {0}", hcrd);
        
            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Business_Transactions"));

            if (businessTransactionsList != null)
            {
                int numExplicitBTs = businessTransactionsList.Where(b => b.BTType != "OVERFLOW" && b.IsExplicitRule == true).Count();
                int numAutodetectedBTs = businessTransactionsList.Where(b => b.BTType != "OVERFLOW" && b.IsExplicitRule == false).Count();

                if (numExplicitBTs + numAutodetectedBTs == 0)
                {
                    healthCheckRuleResult.Grade = 1;
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has no Explicit or Autodetected Transactions", jobTarget.Application);
                }
                else
                {
                    decimal percentageExplicit = Math.Round(((decimal)numExplicitBTs / (decimal)(numExplicitBTs + numAutodetectedBTs) * 100), 0);

                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has '{1}'% Explicit Business Transactions ('{2}' explicit, '{3}' autodetected)", jobTarget.Application, percentageExplicit, numExplicitBTs, numAutodetectedBTs);

                    if (percentageExplicit <= 5)
                    {
                        healthCheckRuleResult.Grade = 1;
                    }
                    else if (percentageExplicit <= 30)
                    {
                        healthCheckRuleResult.Grade = 2;
                    }
                    else if (percentageExplicit <= 50)
                    {
                        healthCheckRuleResult.Grade = 3;
                    }
                    else if (percentageExplicit <= 70)
                    {
                        healthCheckRuleResult.Grade = 4;
                    }
                    else
                    {
                        healthCheckRuleResult.Grade = 5;
                    }
                }
            }
            else
            {
                healthCheckRuleResult.Grade = 1;
                healthCheckRuleResult.Description = String.Format("The Application '{0}' has no Business Transactions to evaluate", jobTarget.Application);
            }

            return healthCheckRuleResult;
        }

        private List<HealthCheckRuleResult> evaluate_APMBusinessTransaction_Type_And_Activity(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMBusinessTransaction> businessTransactionsList,
            List<APMBusinessTransaction> businessTransactionsMetricsList)
        {
            logger.Trace("Evaluating {0}", hcrd);
        
            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMBusinessTransaction", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (businessTransactionsList != null && businessTransactionsMetricsList != null)
            {
                foreach (APMBusinessTransaction businessTransaction in businessTransactionsList)
                {
                    APMBusinessTransaction businessTransactionWithActivity = businessTransactionsMetricsList.Where(b => b.BTID == businessTransaction.BTID).FirstOrDefault();

                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("bt", businessTransaction.BTName, businessTransaction.BTID)));

                    healthCheckRuleResult1.EntityName = businessTransaction.BTName;
                    
                    if (businessTransaction.IsExplicitRule == true)
                    {
                        if (businessTransactionWithActivity != null)
                        {
                            if (businessTransactionWithActivity.HasActivity == true)
                            {
                                healthCheckRuleResult1.Grade = 5;
                                healthCheckRuleResult1.Description = String.Format("The Business Transaction '{0}\\{1} [{2}]' is registered by explicit rule '{3}' and has activity", businessTransaction.TierName, businessTransaction.BTName, businessTransaction.BTType, businessTransaction.RuleName);
                            }
                            else 
                            {
                                healthCheckRuleResult1.Grade = 4;
                                healthCheckRuleResult1.Description = String.Format("The Business Transaction '{0}\\{1} [{2}]' is registered by explicit rule '{3}' but has no activity", businessTransaction.TierName, businessTransaction.BTName, businessTransaction.BTType, businessTransaction.RuleName);
                            }
                        }
                        else
                        {
                            healthCheckRuleResult1.Grade = 3;
                            healthCheckRuleResult1.Description = String.Format("The Business Transaction '{0}\\{1} [{2}]' is registered by explicit rule '{3}' but activity is unknown", businessTransaction.TierName, businessTransaction.BTName, businessTransaction.BTType, businessTransaction.RuleName);
                        }
                    }
                    else
                    {
                        if (businessTransaction.BTType == "OVERFLOW")
                        {
                            if (businessTransactionWithActivity != null)
                            {
                                if (businessTransactionWithActivity.HasActivity == true)
                                {
                                    healthCheckRuleResult1.Grade = 2;
                                    healthCheckRuleResult1.Description = String.Format("The Business Transaction '{0}\\{1} [{2}]' is Overflow and has activity", businessTransaction.TierName, businessTransaction.BTName, businessTransaction.BTType);
                                }
                                else
                                {
                                    healthCheckRuleResult1.Grade = 3;
                                    healthCheckRuleResult1.Description = String.Format("The Business Transaction '{0}\\{1} [{2}]' is Overflow and has no activity", businessTransaction.TierName, businessTransaction.BTName, businessTransaction.BTType);
                                }
                            }
                            else
                            {
                                healthCheckRuleResult1.Grade = 1;
                                healthCheckRuleResult1.Description = String.Format("The Business Transaction '{0}\\{1} [{2}]' is Overflow but activity is unknown", businessTransaction.TierName, businessTransaction.BTName, businessTransaction.BTType);
                            }
                        }
                        else
                        {
                            if (businessTransactionWithActivity != null)
                            {
                                if (businessTransactionWithActivity.HasActivity == true)
                                {
                                    healthCheckRuleResult1.Grade = 4;
                                    healthCheckRuleResult1.Description = String.Format("The Business Transaction '{0}\\{1} [{2}]' is registered by automatic detection rule and has activity", businessTransaction.TierName, businessTransaction.BTName, businessTransaction.BTType);
                                }
                                else
                                {
                                    healthCheckRuleResult1.Grade = 3;
                                    healthCheckRuleResult1.Description = String.Format("The Business Transaction '{0}\\{1} [{2}]' is registered by automatic detection rule but has no activity", businessTransaction.TierName, businessTransaction.BTName, businessTransaction.BTType);
                                }
                            }
                            else
                            {
                                healthCheckRuleResult1.Grade = 2;
                                healthCheckRuleResult1.Description = String.Format("The Business Transaction '{0}\\{1} [{2}]' is registered by automatic detection rule but activity is unknown", businessTransaction.TierName, businessTransaction.BTName, businessTransaction.BTType);
                            }
                        }
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        private HealthCheckRuleResult evaluate_APMBusinessTransaction_Tiers_With_Overflow_Ratio(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMTier> tiersList,
            List<APMBusinessTransaction> businessTransactionsList)
        {
            logger.Trace("Evaluating {0}", hcrd);
        
            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Business_Transactions"));

            if (tiersList != null && businessTransactionsList != null)
            {
                int numOverflowBTs = businessTransactionsList.Where(b => b.BTType == "OVERFLOW").Count();

                if (numOverflowBTs == 0)
                {
                    healthCheckRuleResult.Grade = 5;
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has 0 Tiers with Overflow Business Transactions", jobTarget.Application);
                }
                else
                {
                    decimal percentageOfTiersWithOverflow = Math.Round(((decimal)numOverflowBTs / (decimal)tiersList.Count * 100), 0);

                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has '{1}' ('{2}'%) Tiers with Overflow Business Transactions", jobTarget.Application, numOverflowBTs, percentageOfTiersWithOverflow);

                    if (percentageOfTiersWithOverflow <= 5)
                    {
                        healthCheckRuleResult.Grade = 5;
                    }
                    else if (percentageOfTiersWithOverflow <= 30)
                    {
                        healthCheckRuleResult.Grade = 4;
                    }
                    else if (percentageOfTiersWithOverflow <= 50)
                    {
                        healthCheckRuleResult.Grade = 3;
                    }
                    else if (percentageOfTiersWithOverflow <= 70)
                    {
                        healthCheckRuleResult.Grade = 2;
                    }
                    else
                    {
                        healthCheckRuleResult.Grade = 1;
                    }
                }
            }
            else
            {
                healthCheckRuleResult.Grade = 1;
                healthCheckRuleResult.Description = String.Format("The Application '{0}' has no Tiers or Business Transactions to evaluate", jobTarget.Application);
            }

            return healthCheckRuleResult;
        }

        private List<HealthCheckRuleResult> evaluate_APMBusinessTransaction_Activity(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMBusinessTransaction> businessTransactionsMetricsList)
        {
            logger.Trace("Evaluating {0}", hcrd);
        
            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMBusinessTransaction", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (businessTransactionsMetricsList != null)
            {
                foreach (APMBusinessTransaction businessTransaction in businessTransactionsMetricsList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();

                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("bt", businessTransaction.BTName, businessTransaction.BTID)));

                    healthCheckRuleResult1.EntityName = businessTransaction.BTName;

                    if (businessTransaction.HasActivity == true)
                    {
                        if (businessTransaction.BTType == "OVERFLOW")
                        {
                            healthCheckRuleResult1.Grade = 2;
                            healthCheckRuleResult1.Description = String.Format("The Overflow Business Transaction '{0}\\{1} [{2}]' has activity", businessTransaction.TierName, businessTransaction.BTName, businessTransaction.BTType);
                        }
                        else 
                        {
                            healthCheckRuleResult1.Grade = 5;
                            healthCheckRuleResult1.Description = String.Format("The Business Transaction '{0}\\{1} [{2}]' has activity", businessTransaction.TierName, businessTransaction.BTName, businessTransaction.BTType);
                        }
                    }
                    else
                    {
                        if (businessTransaction.BTType == "OVERFLOW")
                        {
                            healthCheckRuleResult1.Grade = 5;
                            healthCheckRuleResult1.Description = String.Format("The Overflow Business Transaction '{0}\\{1} [{2}]' has no activity", businessTransaction.TierName, businessTransaction.BTName, businessTransaction.BTType);
                        }
                        else
                        {
                            healthCheckRuleResult1.Grade = 4;
                            healthCheckRuleResult1.Description = String.Format("The Business Transaction '{0}\\{1} [{2}]' has no activity", businessTransaction.TierName, businessTransaction.BTName, businessTransaction.BTType);
                        }
                    }
                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMBusinessTransaction_Renamed(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMBusinessTransaction> businessTransactionsList)
        {
            logger.Trace("Evaluating {0}", hcrd);
        
            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMBusinessTransaction", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (businessTransactionsList != null)
            {
                foreach (APMBusinessTransaction businessTransaction in businessTransactionsList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();

                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("bt", businessTransaction.BTName, businessTransaction.BTID)));

                    healthCheckRuleResult1.EntityName = businessTransaction.BTName;

                    if (businessTransaction.IsRenamed == true)
                    {
                        healthCheckRuleResult1.Grade = 3;
                        healthCheckRuleResult1.Description = String.Format("The Business Transaction '{0}\\{1} [{2}]' has been renamed from '{3}'", businessTransaction.TierName, businessTransaction.BTName, businessTransaction.BTType, businessTransaction.BTNameOriginal);

                        healthCheckRuleResults.Add(healthCheckRuleResult1);
                    }
                }
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMBusinessTransaction_Error_Rate(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMBusinessTransaction> businessTransactionsMetricsList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMBusinessTransaction", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (businessTransactionsMetricsList != null)
            {
                foreach (APMBusinessTransaction businessTransaction in businessTransactionsMetricsList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.Grade = 5;

                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("bt", businessTransaction.BTName, businessTransaction.BTID)));

                    healthCheckRuleResult1.EntityName = businessTransaction.BTName;

                    healthCheckRuleResult1.Description = String.Format("The Business Transaction '{0}\\{1} [{2}]' has {3}% Error rate", businessTransaction.TierName, businessTransaction.BTName, businessTransaction.BTType, businessTransaction.ErrorsPercentage);

                    if (businessTransaction.ErrorsPercentage <= getIntegerSetting(healthCheckSettingsDictionary, "APMBusinessTransactionErrorRateGrade5", 20))
                    {
                        healthCheckRuleResult1.Grade = 5;
                    }
                    else if (businessTransaction.ErrorsPercentage <= getIntegerSetting(healthCheckSettingsDictionary, "APMBusinessTransactionErrorRateGrade4", 40))
                    {
                        healthCheckRuleResult1.Grade = 4;
                    }
                    else if (businessTransaction.ErrorsPercentage <= getIntegerSetting(healthCheckSettingsDictionary, "APMBusinessTransactionErrorRateGrade4", 60))
                    {
                        healthCheckRuleResult1.Grade = 3;
                    }
                    else if (businessTransaction.ErrorsPercentage <= getIntegerSetting(healthCheckSettingsDictionary, "APMBusinessTransactionErrorRateGrade2", 80))
                    {
                        healthCheckRuleResult1.Grade = 2;
                    }
                    else
                    {
                        healthCheckRuleResult1.Grade = 1;
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        #endregion

        #region Backends

        private HealthCheckRuleResult evaluate_APMBackend_Number_Of_Entities(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMBackend> backendsList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Backends"));

            if (backendsList != null)
            {
                int numBackends = backendsList.Count;

                if (numBackends == 0)
                {
                    healthCheckRuleResult.Grade = 1;
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has 0 Backends", jobTarget.Application);
                }
                else if (numBackends <= getIntegerSetting(healthCheckSettingsDictionary, "APMBackendCountGrade5", 500))
                {
                    healthCheckRuleResult.Grade = 5;
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has a normal number of Backends ('{1}'<'{2}')", jobTarget.Application, numBackends, getIntegerSetting(healthCheckSettingsDictionary, "APMBackendCountGrade5", 500));
                }
                else if (numBackends <= getIntegerSetting(healthCheckSettingsDictionary, "APMBackendCountGrade4", 1000))
                {
                    healthCheckRuleResult.Grade = 4;
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has a wee bit too many of Backends ('{1}'<'{2}')", jobTarget.Application, numBackends, getIntegerSetting(healthCheckSettingsDictionary, "APMBackendCountGrade4", 1000));
                }
                else if (numBackends <= getIntegerSetting(healthCheckSettingsDictionary, "APMBackendCountGrade3", 1500))
                {
                    healthCheckRuleResult.Grade = 3;
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has kind of a lot of Backends ('{1}'<'{2}')", jobTarget.Application, numBackends, getIntegerSetting(healthCheckSettingsDictionary, "APMBackendCountGrade3", 1500));
                }
                else if (numBackends <= getIntegerSetting(healthCheckSettingsDictionary, "APMBackendCountGrade2", 2000))
                {
                    healthCheckRuleResult.Grade = 2;
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has a lot of Backends ('{1}'<'{2}')", jobTarget.Application, numBackends, getIntegerSetting(healthCheckSettingsDictionary, "APMBackendCountGrade2", 2000));
                }
                else
                {
                    healthCheckRuleResult.Grade = 1;
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' has way too many of Backends ('{1}'>'{2}')", jobTarget.Application, numBackends, getIntegerSetting(healthCheckSettingsDictionary, "APMBackendCountGrade2", 2000));
                }
            }
            else
            {
                healthCheckRuleResult.Grade = 1;
                healthCheckRuleResult.Description = String.Format("The Application '{0}' has 0 Backends to evaluate", jobTarget.Application);
            }

            return healthCheckRuleResult;
        }

        private List<HealthCheckRuleResult> evaluate_APMBackend_Automatic_Or_Explicit(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMBackend> backendsList)
        {
            logger.Trace("Evaluating {0}", hcrd);
        
            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMBackend", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (backendsList != null)
            {
                foreach (APMBackend backend in backendsList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();

                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("back", backend.BackendName, backend.BackendID)));

                    healthCheckRuleResult1.EntityName = backend.BackendName;

                    if (backend.IsExplicitRule == true)
                    {
                        healthCheckRuleResult1.Grade = 5;
                        healthCheckRuleResult1.Description = String.Format("The Backend '{0} [{1}]' is registered by explicit '{2}' rule", backend.BackendName, backend.BackendType, backend.RuleName);
                    }
                    else
                    {
                        if (backend.Prop1Name.ToLower().StartsWith("all other traffic") == true)
                        {
                            healthCheckRuleResult1.Grade = 1;
                            healthCheckRuleResult1.Description = String.Format("The Backend '{0} [{1}]' is overflow", backend.BackendName, backend.BackendType);
                        }
                        else
                        {
                            healthCheckRuleResult1.Grade = 4;
                            healthCheckRuleResult1.Description = String.Format("The Backend '{0} [{1}]' is registered by automatic detection rule", backend.BackendName, backend.BackendType);
                        }
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }
            
            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMBackend_Activity(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMBackend> backendsMetricsList)
        {
            logger.Trace("Evaluating {0}", hcrd);
        
            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMBackend", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (backendsMetricsList != null)
            {
                foreach (APMBackend backend in backendsMetricsList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();

                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("back", backend.BackendName, backend.BackendID)));

                    healthCheckRuleResult1.EntityName = backend.BackendName;

                    if (backend.HasActivity == true)
                    {
                        healthCheckRuleResult1.Grade = 5;
                        healthCheckRuleResult1.Description = String.Format("The Backend '{0} [{1}]' has activity", backend.BackendName, backend.BackendType);
                    }
                    else
                    {
                        healthCheckRuleResult1.Grade = 3;
                        healthCheckRuleResult1.Description = String.Format("The Backend '{0} [{1}]' has no activity", backend.BackendName, backend.BackendType);
                    }
                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMBackend_Error_Rate(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMBackend> backendssMetricsList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMBackend", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (backendssMetricsList != null)
            {
                foreach (APMBackend backend in backendssMetricsList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.Grade = 5;

                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("back", backend.BackendName, backend.BackendID)));

                    healthCheckRuleResult1.EntityName = backend.BackendName;

                    healthCheckRuleResult1.Description = String.Format("The Backend '{0} [{1}]' has '{2}'% Error rate", backend.BackendName, backend.BackendType, backend.ErrorsPercentage);

                    if (backend.ErrorsPercentage <= getIntegerSetting(healthCheckSettingsDictionary, "APMBackendErrorRateGrade5", 20))
                    {
                        healthCheckRuleResult1.Grade = 5;
                    }
                    else if (backend.ErrorsPercentage <= getIntegerSetting(healthCheckSettingsDictionary, "APMBackendErrorRateGrade4", 40))
                    {
                        healthCheckRuleResult1.Grade = 4;
                    }
                    else if (backend.ErrorsPercentage <= getIntegerSetting(healthCheckSettingsDictionary, "APMBackendnErrorRateGrade4", 60))
                    {
                        healthCheckRuleResult1.Grade = 3;
                    }
                    else if (backend.ErrorsPercentage <= getIntegerSetting(healthCheckSettingsDictionary, "APMBackendErrorRateGrade2", 80))
                    {
                        healthCheckRuleResult1.Grade = 2;
                    }
                    else
                    {
                        healthCheckRuleResult1.Grade = 1;
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        #endregion

        #region Nodes

        private List<HealthCheckRuleResult> evaluate_APMNode_Activity(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMNode> nodesMetricsList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMNode", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (nodesMetricsList != null)
            {
                foreach (APMNode node in nodesMetricsList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.Grade = 5;

                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("node", node.NodeName, node.NodeID)));

                    healthCheckRuleResult1.EntityName = node.NodeName;

                    if (node.HasActivity == true)
                    {
                        healthCheckRuleResult1.Grade = 5;
                        healthCheckRuleResult1.Description = String.Format("The Node '{0}\\{1} [{2}]' has activity", node.TierName, node.NodeName, node.AgentType);
                    }
                    else
                    {
                        healthCheckRuleResult1.Grade = 2;
                        healthCheckRuleResult1.Description = String.Format("The Node '{0}\\{1} [{2}]' has no activity", node.TierName, node.NodeName, node.AgentType);
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMNode_Availability_APM(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMNode> nodesMetricsList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMNode", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (nodesMetricsList != null)
            {
                foreach (APMNode node in nodesMetricsList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.Grade = 5;

                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("node", node.NodeName, node.NodeID)));

                    healthCheckRuleResult1.EntityName = node.NodeName;

                    healthCheckRuleResult1.Description = String.Format("The Node '{0}\\{1} [{2}]' has '{3}'% APM Agent availability", node.TierName, node.NodeName, node.AgentType, node.AvailAgent);
                    if (node.AvailAgent == 0)
                    {
                        healthCheckRuleResult1.Grade = 1;
                    }
                    else if (node.AvailAgent < 20)
                    {
                        healthCheckRuleResult1.Grade = 2;
                    }
                    else if (node.AvailAgent < 50)
                    {
                        healthCheckRuleResult1.Grade = 3;
                    }
                    else if (node.AvailAgent < 70)
                    {
                        healthCheckRuleResult1.Grade = 4;
                    }
                    else
                    {
                        healthCheckRuleResult1.Grade = 5;
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMNode_Availability_Machine(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMNode> nodesMetricsList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMNode", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (nodesMetricsList != null)
            {
                foreach (APMNode node in nodesMetricsList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.Grade = 5;

                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("node", node.NodeName, node.NodeID)));

                    healthCheckRuleResult1.EntityName = node.MachineName;

                    healthCheckRuleResult1.Description = String.Format("The Node '{0}\\{1} [{2}]' has '{3}'% Machine Agent availability", node.TierName, node.NodeName, node.AgentType, node.AvailMachine);
                    if (node.AvailMachine == 0)
                    {
                        healthCheckRuleResult1.Grade = 1;
                    }
                    else if (node.AvailMachine < 20)
                    {
                        healthCheckRuleResult1.Grade = 2;
                    }
                    else if (node.AvailMachine < 50)
                    {
                        healthCheckRuleResult1.Grade = 3;
                    }
                    else if (node.AvailMachine < 70)
                    {
                        healthCheckRuleResult1.Grade = 4;
                    }
                    else
                    {
                        healthCheckRuleResult1.Grade = 5;
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMNode_Name_SpecialCharacters(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMNode> nodesList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMNode", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (nodesList != null)
            {
                foreach (APMNode node in nodesList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.Grade = 5;

                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("node", node.NodeName, node.NodeID)));

                    healthCheckRuleResult1.EntityName = node.NodeName;
                    healthCheckRuleResult1.Description = String.Format("The Node '{0}\\{1} [{2}]' has no warning characters", node.TierName, node.NodeName, node.AgentType);

                    bool hasWarningCharacters = false;
                    foreach (var c in getStringSetting(healthCheckSettingsDictionary, "APMNodeNameWarningCharacters", @"` |\/?&").ToCharArray())
                    {
                        if (node.NodeName.Contains(c) == true)
                        {
                            hasWarningCharacters = true;

                            HealthCheckRuleResult healthCheckRuleResult2 = healthCheckRuleResult1.Clone();
                            healthCheckRuleResult2.Grade = 2;

                            healthCheckRuleResult2.Description = String.Format("The Node '{0}\\{1} [{2}]' contains warning character '{3}'", node.TierName, node.NodeName, node.AgentType, c);

                            healthCheckRuleResults.Add(healthCheckRuleResult2);
                        }
                    }
                    if (hasWarningCharacters == false)
                    {
                        healthCheckRuleResults.Add(healthCheckRuleResult1);
                    }
                }
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMNode_Error_Rate(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMNode> nodesMetricsList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMNode", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (nodesMetricsList != null)
            {
                foreach (APMNode node in nodesMetricsList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.Grade = 5;

                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("node", node.NodeName, node.NodeID)));

                    healthCheckRuleResult1.EntityName = node.NodeName;

                    healthCheckRuleResult1.Description = String.Format("The Node '{0}\\{1} [{2}]' has '{3}'% Error rate", node.TierName, node.NodeName, node.AgentType, node.ErrorsPercentage);

                    if (node.ErrorsPercentage <= getIntegerSetting(healthCheckSettingsDictionary, "APMNodeErrorRateGrade5", 20))
                    {
                        healthCheckRuleResult1.Grade = 5;
                    }
                    else if (node.ErrorsPercentage <= getIntegerSetting(healthCheckSettingsDictionary, "APMNodeErrorRateGrade4", 40))
                    {
                        healthCheckRuleResult1.Grade = 4;
                    }
                    else if (node.ErrorsPercentage <= getIntegerSetting(healthCheckSettingsDictionary, "APMNodeErrorRateGrade4", 60))
                    {
                        healthCheckRuleResult1.Grade = 3;
                    }
                    else if (node.ErrorsPercentage <= getIntegerSetting(healthCheckSettingsDictionary, "APMNodeErrorRateGrade2", 80))
                    {
                        healthCheckRuleResult1.Grade = 2;
                    }
                    else
                    {
                        healthCheckRuleResult1.Grade = 1;
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMNode_AgentVersion_APM(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMNode> nodesList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMNode", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (nodesList != null)
            {
                foreach (APMNode node in nodesList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("node", node.NodeName, node.NodeID)));
                    healthCheckRuleResult1.EntityName = node.NodeName;

                    if (node.AgentPresent == false || node.AgentVersion.Length == 0)
                    {
                        healthCheckRuleResult1.Grade = 1;
                        healthCheckRuleResult1.Description = String.Format("The Node '{0}\\{1} [{2}]' does not have an APM Agent deployed", node.TierName, node.NodeName, node.AgentType, node.AgentVersion);
                    }
                    else
                    {
                        healthCheckRuleResult1.Description = String.Format("The Agent '{0}\\{1} [{2}]' is version '{3}'", node.TierName, node.NodeName, node.AgentType, node.AgentVersion);

                        Version nodeVersion = new Version(node.AgentVersion);

                        if (nodeVersion >= getVersionSetting(healthCheckSettingsDictionary, "APMAgentVersionGrade5", "4.5"))
                        {
                            healthCheckRuleResult1.Grade = 5;
                        }
                        else if (nodeVersion >= getVersionSetting(healthCheckSettingsDictionary, "APMAgentVersionGrade4", "4.4"))
                        {
                            healthCheckRuleResult1.Grade = 4;
                        }
                        else if (nodeVersion >= getVersionSetting(healthCheckSettingsDictionary, "APMAgentVersionGrade3", "4.3"))
                        {
                            healthCheckRuleResult1.Grade = 3;
                        }
                        else if (nodeVersion >= getVersionSetting(healthCheckSettingsDictionary, "APMAgentVersionGrade2", "4.2"))
                        {
                            healthCheckRuleResult1.Grade = 2;
                        }
                        else
                        {
                            healthCheckRuleResult1.Grade = 1;
                        }
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMNode_AgentVersion_Machine(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMNode> nodesList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMNode", jobTarget.Application, jobTarget.ApplicationID, hcrd);

            if (nodesList != null)
            {
                foreach (APMNode node in nodesList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), getShortenedEntityNameForWordBookmark("node", node.NodeName, node.NodeID)));
                    healthCheckRuleResult1.EntityName = node.NodeName;

                    if (node.MachineAgentPresent == false || node.MachineAgentVersion.Length == 0)
                    {
                        healthCheckRuleResult1.Grade = 1;
                        healthCheckRuleResult1.Description = String.Format("The Node '{0}\\{1} [{2}]' does not have a Machine Agent deployed", node.TierName, node.NodeName, node.AgentType);
                    }
                    else
                    {
                        healthCheckRuleResult1.Description = String.Format("The Machine '{0}\\{1} [{2}]' is version '{3}'", node.TierName, node.MachineName, node.AgentType, node.MachineAgentVersion);

                        Version nodeVersion = new Version(node.MachineAgentVersion);

                        if (nodeVersion >= getVersionSetting(healthCheckSettingsDictionary, "APMMachineAgentVersionGrade5", "4.5"))
                        {
                            healthCheckRuleResult1.Grade = 5;
                        }
                        else if (nodeVersion >= getVersionSetting(healthCheckSettingsDictionary, "APMMachineAgentVersionGrade4", "4.4"))
                        {
                            healthCheckRuleResult1.Grade = 4;
                        }
                        else if (nodeVersion >= getVersionSetting(healthCheckSettingsDictionary, "APMMachineAgentVersionGrade3", "4.3"))
                        {
                            healthCheckRuleResult1.Grade = 3;
                        }
                        else if (nodeVersion >= getVersionSetting(healthCheckSettingsDictionary, "APMMachineAgentVersionGrade2", "4.2"))
                        {
                            healthCheckRuleResult1.Grade = 2;
                        }
                        else
                        {
                            healthCheckRuleResult1.Grade = 1;
                        }
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        #endregion

        #region Application Configuration

        private HealthCheckRuleResult evaluate_APMApplication_BTLockdown(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMApplicationConfiguration> applicationConfigurationsList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application"));
            healthCheckRuleResult.Grade = 1;
            healthCheckRuleResult.Description = String.Format("The Application '{0}' Business Transaction Lockdown setting is unknown", jobTarget.Application);

            if (applicationConfigurationsList != null && applicationConfigurationsList.Count > 0)
            {
                APMApplicationConfiguration applicationConfiguration = applicationConfigurationsList[0];

                healthCheckRuleResult.Description = String.Format("The Application '{0}' Business Transaction Lockdown setting is set to '{1}'", jobTarget.Application, applicationConfiguration.IsBTLockdownEnabled);

                if (applicationConfiguration.IsBTLockdownEnabled == true)
                {
                    healthCheckRuleResult.Grade = 5;
                }
                else
                {
                    healthCheckRuleResult.Grade = 4;
                }
            }

            return healthCheckRuleResult;
        }

        private HealthCheckRuleResult evaluate_APMApplication_BTScope_Config(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMApplicationConfiguration> applicationConfigurationsList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application"));
            healthCheckRuleResult.Grade = 1;
            healthCheckRuleResult.Description = String.Format("The Application '{0}' Business Transaction Configuration Scopes setting is unknown", jobTarget.Application);

            if (applicationConfigurationsList != null && applicationConfigurationsList.Count > 0)
            {
                APMApplicationConfiguration applicationConfiguration = applicationConfigurationsList[0];

                if (applicationConfiguration.IsBT20ConfigEnabled == true)
                {
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' Business Transaction Configuration is using Scopes-based configuration model", jobTarget.Application);
                    healthCheckRuleResult.Grade = 5;
                }
                else
                {
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' Business Transaction Configuration is using legacy Tier-based configuration model", jobTarget.Application);
                    healthCheckRuleResult.Grade = 2;
                }
            }

            return healthCheckRuleResult;
        }

        private HealthCheckRuleResult evaluate_APMApplication_BTCleanup(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMApplicationConfiguration> applicationConfigurationsList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application"));
            healthCheckRuleResult.Grade = 1;
            healthCheckRuleResult.Description = String.Format("The Application '{0}' Business Transaction Cleanup setting is unknown", jobTarget.Application);

            if (applicationConfigurationsList != null && applicationConfigurationsList.Count > 0)
            {
                APMApplicationConfiguration applicationConfiguration = applicationConfigurationsList[0];

                healthCheckRuleResult.Description = String.Format("The Application '{0}' Business Transaction Cleanup setting is set to '{1}'", jobTarget.Application, applicationConfiguration.IsBTCleanupEnabled);

                if (applicationConfiguration.IsBTCleanupEnabled == true)
                {
                    healthCheckRuleResult.Grade = 5;
                }
                else
                {
                    healthCheckRuleResult.Grade = 2;
                }
            }

            return healthCheckRuleResult;
        }

        private HealthCheckRuleResult evaluate_APMApplication_BTCleanup_Interval(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMApplicationConfiguration> applicationConfigurationsList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application"));
            healthCheckRuleResult.Grade = 1;
            healthCheckRuleResult.Description = String.Format("The Application '{0}' Business Transaction Cleanup Interval setting is unknown", jobTarget.Application);

            if (applicationConfigurationsList != null && applicationConfigurationsList.Count > 0)
            {
                APMApplicationConfiguration applicationConfiguration = applicationConfigurationsList[0];

                if (applicationConfiguration.IsBTCleanupEnabled == true)
                {
                    if (applicationConfiguration.BTCleanupInterval != 15)
                    {
                        healthCheckRuleResult.Grade = 2;
                        healthCheckRuleResult.Description = String.Format("The Application '{0}' Business Transaction Cleanup Interval setting is '{1}' which is not default", jobTarget.Application, applicationConfiguration.BTCleanupInterval);
                    }
                    else
                    {
                        healthCheckRuleResult.Description = String.Format("The Application '{0}' Business Transaction Cleanup Interval setting is '{1}', default", jobTarget.Application, applicationConfiguration.BTCleanupInterval);
                        healthCheckRuleResult.Grade = 5;
                    }
                }
                else
                {
                    healthCheckRuleResult.Grade = 5;
                    healthCheckRuleResult.Description = String.Format("The Application '{0}' Business Transaction Cleanup Interval is '{1}' but is ignored since Business Transaction Cleanup is not on", jobTarget.Application, applicationConfiguration.BTCleanupInterval);
                }
            }

            return healthCheckRuleResult;
        }

        private HealthCheckRuleResult evaluate_APMApplication_BT_Developer_Mode(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<APMApplicationConfiguration> applicationConfigurationsList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application"));
            healthCheckRuleResult.Grade = 1;
            healthCheckRuleResult.Description = String.Format("The Application '{0}' Business Transaction Developer Mode setting is unknown", jobTarget.Application);

            if (applicationConfigurationsList != null && applicationConfigurationsList.Count > 0)
            {
                APMApplicationConfiguration applicationConfiguration = applicationConfigurationsList[0];

                healthCheckRuleResult.Description = String.Format("The Application '{0}' Business Transaction Developer Mode setting is set to '{1}'", jobTarget.Application, applicationConfiguration.IsDeveloperModeEnabled);

                if (applicationConfiguration.IsDeveloperModeEnabled == true)
                {
                    healthCheckRuleResult.Grade = 2;
                }
                else
                {
                    healthCheckRuleResult.Grade = 5;
                }
            }

            return healthCheckRuleResult;
        }

        private List<HealthCheckRuleResult> evaluate_APMApplication_BT_Discovery_Rules_Defaults(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<BusinessTransactionDiscoveryRule20> businessTransactionDiscoveryRules20List,
            List<BusinessTransactionDiscoveryRule20> businessTransactionDiscoveryRules20TemplateList,
            List<ConfigurationDifference> configurationDifferencesList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application"));

            if (businessTransactionDiscoveryRules20List != null && businessTransactionDiscoveryRules20TemplateList != null)
            {
                // Loop through the template configuration, trying to see if the default values exist, and if yes, whether they differ
                foreach (BusinessTransactionDiscoveryRule20 businessTransactionDiscoveryRule20Template in businessTransactionDiscoveryRules20TemplateList)
                {
                    BusinessTransactionDiscoveryRule20 businessTransactionDiscoveryRule20 = businessTransactionDiscoveryRules20List.Where(b => String.Compare(b.EntityIdentifier, businessTransactionDiscoveryRule20Template.EntityIdentifier, true) == 0).FirstOrDefault();
                    if (businessTransactionDiscoveryRule20 == null)
                    {
                        // No rule with that name
                        HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                        healthCheckRuleResult1.EntityType = "APMBTDiscoveryRule";
                        healthCheckRuleResult1.EntityName = businessTransactionDiscoveryRule20Template.RuleName;
                        healthCheckRuleResult1.Grade = 3;
                        healthCheckRuleResult1.Description = String.Format("The Business Transaction Detection rule '{0}' that is present in default settings is absent", businessTransactionDiscoveryRule20Template.EntityIdentifier);
                        healthCheckRuleResults.Add(healthCheckRuleResult1);
                    }
                    else
                    {
                        // Found matching rule in the target application configuration for this rule that is always in the template

                        // Find the DIFFERENT differences
                        if (configurationDifferencesList != null)
                        {
                            List<ConfigurationDifference> configurationDifferences_Different =
                                configurationDifferencesList.Where(c =>
                                    c.Difference == DIFFERENCE_DIFFERENT && 
                                    c.RuleType == businessTransactionDiscoveryRule20.RuleType && 
                                    c.EntityIdentifier == businessTransactionDiscoveryRule20.EntityIdentifier).ToList();

                            if (configurationDifferences_Different != null && configurationDifferences_Different.Count > 0)
                            {
                                int numberOfLessCriticalDifferences = 0;

                                foreach (ConfigurationDifference configurationDifference in configurationDifferences_Different)
                                {
                                    // Enumerate DIFFERENT differences, looking for important ones
                                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                                    healthCheckRuleResult1.EntityType = "APMBTDiscoveryRule";
                                    healthCheckRuleResult1.EntityName = businessTransactionDiscoveryRule20.RuleName;
                                    healthCheckRuleResult1.Description = String.Format("The Business Transaction Detection rule '{0}' property '{1}' is '{2}', different from default '{3}'", businessTransactionDiscoveryRule20.EntityIdentifier, configurationDifference.Property, configurationDifference.DifferenceValue, configurationDifference.ReferenceValue);

                                    switch (configurationDifference.Property)
                                    {
                                        case "IsDiscoveryEnabled":
                                            healthCheckRuleResult1.Grade = 2;
                                            healthCheckRuleResults.Add(healthCheckRuleResult1);

                                            break;

                                        case "IsMonitoringEnabled":
                                            healthCheckRuleResult1.Grade = 2;
                                            healthCheckRuleResults.Add(healthCheckRuleResult1);

                                            break;

                                        case "Version":
                                        case "Description":
                                            // Ignore
                                            break;

                                        default:
                                            numberOfLessCriticalDifferences++;
                                            break;
                                    }
                                }

                                if (numberOfLessCriticalDifferences > 0)
                                {
                                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                                    healthCheckRuleResult1.EntityType = "APMBTDiscoveryRule";
                                    healthCheckRuleResult1.EntityName = businessTransactionDiscoveryRule20.RuleName;
                                    healthCheckRuleResult1.Grade = 4;
                                    healthCheckRuleResult1.Description = String.Format("The Business Transaction Detection rule '{0}' has '{1}' less important properties different from default", businessTransactionDiscoveryRule20.EntityIdentifier, numberOfLessCriticalDifferences);
                                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                                }
                            }
                        }
                    }
                }
            }

            if (healthCheckRuleResults.Count == 0)
            {
                HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                healthCheckRuleResult1.Grade = 5;
                healthCheckRuleResult1.Description = String.Format("The Business Transaction Detection rules are identical to default ones");

                healthCheckRuleResults.Add(healthCheckRuleResult1);
            }
            else
            {
                HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                healthCheckRuleResult1.Grade = 4;
                healthCheckRuleResult1.Description = String.Format("The Business Transaction Detection rules are different from default ones");

                healthCheckRuleResults.Add(healthCheckRuleResult1);
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMApplication_BT_Entry_Rules_Defaults(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<BusinessTransactionEntryRule20> businessTransactionEntryRules20List,
            List<BusinessTransactionEntryRule20> businessTransactionEntryRules20TemplateList,
            List<ConfigurationDifference> configurationDifferencesList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application"));

            if (businessTransactionEntryRules20List != null && businessTransactionEntryRules20TemplateList != null)
            {
                // Loop through the template configuration, trying to see if the default values exist, and if yes, whether they differ
                foreach (BusinessTransactionEntryRule20 businessTransactionEntryRule20Template in businessTransactionEntryRules20TemplateList)
                {
                    BusinessTransactionEntryRule20 businessTransactionEntryRule20 = businessTransactionEntryRules20List.Where(b => String.Compare(b.EntityIdentifier, businessTransactionEntryRule20Template.EntityIdentifier, true) == 0).FirstOrDefault();
                    if (businessTransactionEntryRule20 == null)
                    {
                        // No rule with that name
                        HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                        healthCheckRuleResult1.EntityType = "APMBTEntryRule20";
                        healthCheckRuleResult1.EntityName = businessTransactionEntryRule20Template.RuleName;
                        healthCheckRuleResult1.Grade = 3;
                        healthCheckRuleResult1.Description = String.Format("The Business Transaction Detection rule '{0}' that is present in default settings is absent", businessTransactionEntryRule20Template.EntityIdentifier);
                        healthCheckRuleResults.Add(healthCheckRuleResult1);
                    }
                    else
                    {
                        // Found matching rule in the target application configuration for this rule that is always in the template

                        // Find the DIFFERENT differences
                        if (configurationDifferencesList != null)
                        {
                            List<ConfigurationDifference> configurationDifferences_Different =
                                configurationDifferencesList.Where(c =>
                                    c.Difference == DIFFERENCE_DIFFERENT &&
                                    c.RuleType == businessTransactionEntryRule20.RuleType &&
                                    c.EntityIdentifier == businessTransactionEntryRule20.EntityIdentifier).ToList();

                            if (configurationDifferences_Different != null && configurationDifferences_Different.Count > 0)
                            {
                                int numberOfLessCriticalDifferences = 0;

                                foreach (ConfigurationDifference configurationDifference in configurationDifferences_Different)
                                {
                                    // Enumerate DIFFERENT differences, looking for important ones
                                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                                    healthCheckRuleResult1.EntityType = "APMBTEntryRule20";
                                    healthCheckRuleResult1.EntityName = businessTransactionEntryRule20.RuleName;
                                    healthCheckRuleResult1.Description = String.Format("The Business Transaction Entry rule '{0}' property '{1}' is '{2}', different from default '{3}'", businessTransactionEntryRule20.EntityIdentifier, configurationDifference.Property, configurationDifference.DifferenceValue, configurationDifference.ReferenceValue);

                                    switch (configurationDifference.Property)
                                    {
                                        case "ScopeName":
                                            healthCheckRuleResult1.Grade = 4;
                                            healthCheckRuleResults.Add(healthCheckRuleResult1);

                                            break;

                                        case "IsEnabled":
                                            healthCheckRuleResult1.Grade = 2;
                                            healthCheckRuleResults.Add(healthCheckRuleResult1);

                                            break;

                                        case "Version":
                                        case "Description":
                                            // Ignore
                                            break;

                                        default:
                                            numberOfLessCriticalDifferences++;
                                            break;
                                    }
                                }

                                if (numberOfLessCriticalDifferences > 0)
                                {
                                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                                    healthCheckRuleResult1.EntityType = "APMBTEntryRule20";
                                    healthCheckRuleResult1.EntityName = businessTransactionEntryRule20.RuleName;
                                    healthCheckRuleResult1.Grade = 4;
                                    healthCheckRuleResult1.Description = String.Format("The Business Transaction Entry rule '{0}' has '{1}' less important properties different from default", businessTransactionEntryRule20.EntityIdentifier, numberOfLessCriticalDifferences);
                                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                                }
                            }
                        }
                    }
                }
            }

            if (healthCheckRuleResults.Count == 0)
            {
                HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                healthCheckRuleResult1.Grade = 5;
                healthCheckRuleResult1.Description = String.Format("The Business Transaction Entry rules are identical to default ones");

                healthCheckRuleResults.Add(healthCheckRuleResult1);
            }
            else
            {
                HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                healthCheckRuleResult1.Grade = 4;
                healthCheckRuleResult1.Description = String.Format("The Business Transaction Entry rules are different from default ones");

                healthCheckRuleResults.Add(healthCheckRuleResult1);
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMApplication_BT_Scopes_Configuration(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<BusinessTransactionEntryScope> businessTransactionEntryScopesList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application"));

            if (businessTransactionEntryScopesList != null)
            {
                foreach (BusinessTransactionEntryScope businessTransactionEntryScope in businessTransactionEntryScopesList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.EntityType = "APMBTScope";
                    healthCheckRuleResult1.EntityName = businessTransactionEntryScope.ScopeName;

                    if (businessTransactionEntryScope.NumRules == 0)
                    {
                        healthCheckRuleResult1.Grade = 1;

                        healthCheckRuleResult1.Description = String.Format("The Business Transaction Scope '{0} [{1}]' has no rules assigned to it", businessTransactionEntryScope.ScopeName, businessTransactionEntryScope.ScopeType);
                    }
                    else
                    {
                        switch (businessTransactionEntryScope.ScopeType)
                        {
                            case "ALL_TIERS_IN_APP":
                                healthCheckRuleResult1.Grade = 5;
                                healthCheckRuleResult1.Description = String.Format("The Business Transaction Scope '{0} [{1}]' with '{2}' rules assigned to it is configured properly", businessTransactionEntryScope.ScopeName, businessTransactionEntryScope.ScopeType, businessTransactionEntryScope.NumRules);

                                break;

                            case "SELECTED_TIERS":
                                if (businessTransactionEntryScope.NumTiers == 0)
                                {
                                    healthCheckRuleResult1.Grade = 2;
                                    healthCheckRuleResult1.Description = String.Format("The Business Transaction Scope '{0} [{1}]' with '{2}' rules assigned to it has no tiers assigned", businessTransactionEntryScope.ScopeName, businessTransactionEntryScope.ScopeType, businessTransactionEntryScope.NumRules);
                                }
                                else
                                {
                                    healthCheckRuleResult1.Grade = 5;
                                    healthCheckRuleResult1.Description = String.Format("The Business Transaction Scope '{0} [{1}]' with '{2}' rules assigned to it has '{3}' tiers ('{4}') assigned", businessTransactionEntryScope.ScopeName, businessTransactionEntryScope.ScopeType, businessTransactionEntryScope.NumRules, businessTransactionEntryScope.NumTiers, businessTransactionEntryScope.AffectedTiers);
                                }
                                break;

                            case "ALL_TIERS_IN_APP_EXCEPT":
                                if (businessTransactionEntryScope.NumTiers == 0)
                                {
                                    healthCheckRuleResult1.Grade = 4;
                                    healthCheckRuleResult1.Description = String.Format("The Business Transaction Scope '{0} [{1}]' with '{2}' rules assigned to it has no tiers excluded", businessTransactionEntryScope.ScopeName, businessTransactionEntryScope.ScopeType, businessTransactionEntryScope.NumRules);
                                }
                                else
                                {
                                    healthCheckRuleResult1.Grade = 5;
                                    healthCheckRuleResult1.Description = String.Format("The Business Transaction Scope '{0} [{1}]' with '{2}' rules assigned to it has '{3}' tiers ('{4}') excluded", businessTransactionEntryScope.ScopeName, businessTransactionEntryScope.ScopeType, businessTransactionEntryScope.NumRules, businessTransactionEntryScope.NumTiers, businessTransactionEntryScope.AffectedTiers);
                                }
                                break;

                            default:
                                break;
                        }
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMApplication_BT_Rules_Detection_Results(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<BusinessTransactionEntryRule20> businessTransactionEntryRules20List)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application"));

            if (businessTransactionEntryRules20List != null)
            {
                foreach (BusinessTransactionEntryRule20 businessTransactionEntryRule20 in businessTransactionEntryRules20List)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.EntityType = "APMBTEntryRule20";
                    healthCheckRuleResult1.EntityName = businessTransactionEntryRule20.RuleName;

                    if (businessTransactionEntryRule20.ScopeName.Length == 0)
                    {
                        healthCheckRuleResult1.Grade = 1;
                        healthCheckRuleResult1.Description = String.Format("The Business Transaction Rule '{0} [{1}]' is not assigned to any scope, so it won't be effective", businessTransactionEntryRule20.RuleName, businessTransactionEntryRule20.EntryPointType);
                    }
                    else
                    {
                        if (businessTransactionEntryRule20.IsBuiltIn == true)
                        {
                            healthCheckRuleResult1.Grade = 5;
                            healthCheckRuleResult1.Description = String.Format("The Business Transaction Rule '{0} [{1}]' is built-in and has '{2}' detected Business Transactions", businessTransactionEntryRule20.RuleName, businessTransactionEntryRule20.EntryPointType, businessTransactionEntryRule20.NumDetectedBTs);
                        }
                        else
                        {
                            // Non-built in rules should have some BTs, right?
                            if (businessTransactionEntryRule20.NumDetectedBTs == 0)
                            {
                                healthCheckRuleResult1.Grade = 3;
                                healthCheckRuleResult1.Description = String.Format("The Business Transaction Rule '{0} [{1}]' is custom but has no detected Business Transactions", businessTransactionEntryRule20.RuleName, businessTransactionEntryRule20.EntryPointType);
                            }
                            else
                            {
                                healthCheckRuleResult1.Grade = 5;
                                healthCheckRuleResult1.Description = String.Format("The Business Transaction Rule '{0} [{1}]' is custom and has '{2}' detected Business Transactions", businessTransactionEntryRule20.RuleName, businessTransactionEntryRule20.EntryPointType, businessTransactionEntryRule20.NumDetectedBTs);
                            }
                        }
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMApplication_Backend_Rules_Detection_Results(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<BackendDiscoveryRule> backendDiscoveryRulesList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application"));

            if (backendDiscoveryRulesList != null)
            {
                foreach (BackendDiscoveryRule backendDiscoveryRule in backendDiscoveryRulesList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.EntityType = "APMBackendRule";
                    healthCheckRuleResult1.EntityName = backendDiscoveryRule.RuleName;

                    if (backendDiscoveryRule.TierName.Length == 0)
                    {
                        if (backendDiscoveryRule.IsBuiltIn == true)
                        {
                            healthCheckRuleResult1.Grade = 5;
                            healthCheckRuleResult1.Description = String.Format("The Backend Detection Rule '{0} [{1}]' for Agent '{2}' is built-in", backendDiscoveryRule.RuleName, backendDiscoveryRule.ExitType, backendDiscoveryRule.AgentType);
                        }
                        else
                        {
                            // Non-built in rules should have some BTs, right?
                            if (backendDiscoveryRule.NumDetectedBackends == 0)
                            {
                                healthCheckRuleResult1.Grade = 3;
                                healthCheckRuleResult1.Description = String.Format("The Backend Detection Rule '{0} [{1}]' for Agent '{2}' is custom but has no detected Backends", backendDiscoveryRule.RuleName, backendDiscoveryRule.ExitType, backendDiscoveryRule.AgentType);
                            }
                            else
                            {
                                healthCheckRuleResult1.Grade = 5;
                                healthCheckRuleResult1.Description = String.Format("The Backend Detection Rule '{0} [{1}]' for Agent '{2}' is custom and has '{3}' detected Backends", backendDiscoveryRule.RuleName, backendDiscoveryRule.ExitType, backendDiscoveryRule.AgentType, backendDiscoveryRule.NumDetectedBackends);
                            }
                        }
                    }
                    else
                    {
                        // It is a tier override
                        // Not so good, knock down all ratings down by 1
                        if (backendDiscoveryRule.IsBuiltIn == true)
                        {
                            healthCheckRuleResult1.Grade = 4;
                            healthCheckRuleResult1.Description = String.Format("The Backend Detection Rule '{0} [{1}]' for Agent '{2}' is built-in, but it is overriden for Tier '{3}'", backendDiscoveryRule.RuleName, backendDiscoveryRule.ExitType, backendDiscoveryRule.AgentType, backendDiscoveryRule.TierName);
                        }
                        else
                        {
                            // Non-built in rules should have some BTs, right?
                            if (backendDiscoveryRule.NumDetectedBackends == 0)
                            {
                                healthCheckRuleResult1.Grade = 2;
                                healthCheckRuleResult1.Description = String.Format("The Backend Detection Rule '{0} [{1}]' for Agent '{2}' is overridden for Tier '{3}', is custom but has no detected Backends", backendDiscoveryRule.RuleName, backendDiscoveryRule.ExitType, backendDiscoveryRule.AgentType, backendDiscoveryRule.TierName);
                            }
                            else
                            {
                                healthCheckRuleResult1.Grade = 4;
                                healthCheckRuleResult1.Description = String.Format("The Backend Detection Rule '{0} [{1}]' for Agent '{2}' is overridden for Tier '{3}', is custom and has '{4}' detected Backends", backendDiscoveryRule.RuleName, backendDiscoveryRule.ExitType, backendDiscoveryRule.AgentType, backendDiscoveryRule.TierName, backendDiscoveryRule.NumDetectedBackends);
                            }
                        }
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        private List<HealthCheckRuleResult> evaluate_APMApplication_Custom_Exit_Rules_Detection_Results(
            HealthCheckRuleDescription hcrd,
            JobTarget jobTarget,
            Dictionary<string, HealthCheckSettingMapping> healthCheckSettingsDictionary,
            JobConfiguration jobConfiguration,
            List<CustomExitRule> customExitRulesList)
        {
            logger.Trace("Evaluating {0}", hcrd);

            List<HealthCheckRuleResult> healthCheckRuleResults = new List<HealthCheckRuleResult>();

            HealthCheckRuleResult healthCheckRuleResult = createHealthCheckRuleResult(jobTarget, "APMApp", jobTarget.Application, jobTarget.ApplicationID, hcrd);
            healthCheckRuleResult.RuleLink = String.Format(@"=HYPERLINK(""{0}"", ""<Detail>"")", String.Format("{0}#{1}", FilePathMap.ApplicationSummaryWordReportFilePath(jobTarget, jobConfiguration.Input.TimeRange, false), "Application"));

            if (customExitRulesList != null)
            {
                foreach (CustomExitRule customExitRule in customExitRulesList)
                {
                    HealthCheckRuleResult healthCheckRuleResult1 = healthCheckRuleResult.Clone();
                    healthCheckRuleResult1.EntityType = "APMCustomExitRule";
                    healthCheckRuleResult1.EntityName = customExitRule.RuleName;

                    if (customExitRule.TierName.Length == 0)
                    {
                        // Non-built in rules should have some BTs, right?
                        if (customExitRule.NumDetectedBackends == 0)
                        {
                            healthCheckRuleResult1.Grade = 3;
                            healthCheckRuleResult1.Description = String.Format("The Custom Exit Rule '{0} [{1}]' for Agent '{2}' has no detected Backends", customExitRule.RuleName, customExitRule.ExitType, customExitRule.AgentType);
                        }
                        else
                        {
                            healthCheckRuleResult1.Grade = 5;
                            healthCheckRuleResult1.Description = String.Format("The Custom Exit Rule '{0} [{1}]' for Agent '{2}' has '{3}' detected Backends", customExitRule.RuleName, customExitRule.ExitType, customExitRule.AgentType, customExitRule.NumDetectedBackends);
                        }
                    }
                    else
                    {
                        // It is a tier override
                        // Not so good, knock down all ratings down by 1
                        // Non-built in rules should have some BTs, right?
                        if (customExitRule.NumDetectedBackends == 0)
                        {
                            healthCheckRuleResult1.Grade = 2;
                            healthCheckRuleResult1.Description = String.Format("The Custom Exit Rule '{0} [{1}]' for Agent '{2}' is overridden for for Tier '{3}' but has no detected Backends", customExitRule.RuleName, customExitRule.ExitType, customExitRule.AgentType, customExitRule.TierName);
                        }
                        else
                        {
                            healthCheckRuleResult1.Grade = 4;
                            healthCheckRuleResult1.Description = String.Format("The Custom Exit Rule '{0} [{1}]' for Agent '{2}' is overridden for for Tier '{3}' and has '{4}' detected Backends", customExitRule.RuleName, customExitRule.ExitType, customExitRule.AgentType, customExitRule.NumDetectedBackends, customExitRule.TierName);
                        }
                    }

                    healthCheckRuleResults.Add(healthCheckRuleResult1);
                }
            }

            return healthCheckRuleResults;
        }

        #endregion
    
        private List<ConfigurationDifference> compareListOfEntities<T>(
            JobTarget jobTargetReference,
            JobTarget jobTargetDifference,
            List<T> configEntitiesListReference,
            List<T> configEntitiesListDifference) where T : ConfigurationEntityBase
        {
            List<ConfigurationDifference> configurationDifferencesList = new List<ConfigurationDifference>(256);

            if (configEntitiesListReference == null) configEntitiesListReference = new List<T>();
            if (configEntitiesListDifference == null) configEntitiesListDifference = new List<T>();
            List<T> configEntitiesListExtra = new List<T>(256);

            // Here is what we get in the reference and difference lists
            // List             List
            // Reference        Difference      Action
            // AAA              AAA             Compare items
            // BBB                              item in Difference is MISSING
            //                  CCC             item in Difference is EXTRA

            // First loop through Reference list looking for matches
            // Remove each item from the reference list until there are no more left
            while (configEntitiesListReference.Count > 0)
            {
                ConfigurationEntityBase configEntityReference = configEntitiesListReference[0];
                configEntitiesListReference.RemoveAt(0);

                // Find match for this configuration entity
                int configEntityDifferenceIndex = configEntitiesListDifference.FindIndex(c => String.Compare(c.EntityIdentifier, configEntityReference.EntityIdentifier, true) == 0);
                if (configEntityDifferenceIndex < 0)
                {
                    // No match. This must be entity BBB, where item in Difference is MISSING
                    ConfigurationDifference configDifference = fillConfigurationDifference(
                        jobTargetReference,
                        jobTargetDifference,
                        configEntityReference,
                        DIFFERENCE_MISSING,
                        PROPERTY_ENTIRE_OBJECT,
                        configEntityReference.ToString(),
                        String.Empty);

                    configurationDifferencesList.Add(configDifference);
                }
                else
                {
                    // Found matching entity AAA. Let's compare them against each other
                    ConfigurationEntityBase configEntityDifference = configEntitiesListDifference[configEntityDifferenceIndex];
                    configEntitiesListDifference.RemoveAt(configEntityDifferenceIndex);

                    configurationDifferencesList.AddRange(compareTwoEntities(jobTargetReference, jobTargetDifference, configEntityReference, configEntityDifference));
                }
            }

            // Whatever is left in Difference list must be like entity CCC, therefore they are all EXTRA
            if (configEntitiesListDifference.Count > 0)
            {
                for (int i = 0; i < configEntitiesListDifference.Count; i++)
                {
                    ConfigurationEntityBase configEntityDifference = configEntitiesListDifference[i];

                    ConfigurationDifference configDifference = fillConfigurationDifference(
                        jobTargetReference,
                        jobTargetDifference,
                        configEntityDifference,
                        DIFFERENCE_EXTRA,
                        PROPERTY_ENTIRE_OBJECT,
                        String.Empty,
                        configEntityDifference.ToString());

                    configurationDifferencesList.Add(configDifference);
                }
            }

            return configurationDifferencesList;
        }

        private List<ConfigurationDifference> compareTwoEntities(
            JobTarget jobTargetReference,
            JobTarget jobTargetDifference,
            ConfigurationEntityBase configEntityReference,
            ConfigurationEntityBase configEntityDifference)
        {
            List<ConfigurationDifference> configurationDifferencesList = new List<ConfigurationDifference>(16);

            // Look through all the properties
            Type configEntityType = configEntityReference.GetType();
            foreach (PropertyInfo prop in configEntityType.GetProperties())
            {
                // Only care about the ones marked for comparison with the superawesome custom attribute
                object[] attributes = prop.GetCustomAttributes(typeof(FieldComparisonAttribute), true);
                if (attributes.Length > 0)
                {
                    FieldComparisonAttribute fieldComparisonAttribute = ((FieldComparisonAttribute)attributes[0]);

                    object propValueReferenceObject = prop.GetValue(configEntityReference);
                    object propValueDifferenceObject = prop.GetValue(configEntityDifference);

                    switch (fieldComparisonAttribute.FieldComparison)
                    {
                        case FieldComparisonType.ValueComparison:
                            // Compare values using the right datatype
                            if (propValueReferenceObject is System.String)
                            {
                                string propValueReferenceString = (string)propValueReferenceObject;
                                string propValueDifferenceString = (string)propValueDifferenceObject;

                                if (String.Compare(propValueReferenceString, propValueDifferenceString, false) != 0)
                                {
                                    ConfigurationDifference configDifference = fillConfigurationDifference(
                                        jobTargetReference,
                                        jobTargetDifference,
                                        configEntityReference,
                                        DIFFERENCE_DIFFERENT,
                                        prop.Name,
                                        propValueReferenceString,
                                        propValueDifferenceString);

                                    configurationDifferencesList.Add(configDifference);
                                }
                            }
                            else if (propValueReferenceObject is System.Int32)
                            {
                                int propValueReferenceInt = (int)propValueReferenceObject;
                                int propValueDifferenceInt = (int)propValueDifferenceObject;

                                if (propValueReferenceInt != propValueDifferenceInt)
                                {
                                    ConfigurationDifference configDifference = fillConfigurationDifference(
                                        jobTargetReference,
                                        jobTargetDifference,
                                        configEntityReference,
                                        DIFFERENCE_DIFFERENT,
                                        prop.Name,
                                        propValueReferenceInt.ToString(),
                                        propValueDifferenceInt.ToString());

                                    configurationDifferencesList.Add(configDifference);
                                }
                            }
                            else if (propValueReferenceObject is System.Int64)
                            {
                                long propValueReferenceLong = (long)propValueReferenceObject;
                                long propValueDifferenceLong = (long)propValueDifferenceObject;

                                if (propValueReferenceLong != propValueDifferenceLong)
                                {
                                    ConfigurationDifference configDifference = fillConfigurationDifference(
                                        jobTargetReference,
                                        jobTargetDifference,
                                        configEntityReference,
                                        DIFFERENCE_DIFFERENT,
                                        prop.Name,
                                        propValueReferenceLong.ToString(),
                                        propValueDifferenceLong.ToString());

                                    configurationDifferencesList.Add(configDifference);
                                }
                            }
                            else if (propValueReferenceObject is System.Boolean)
                            {
                                bool propValueReferenceBoolean = (bool)propValueReferenceObject;
                                bool propValueDifferenceBoolean = (bool)propValueDifferenceObject;

                                if (propValueReferenceBoolean != propValueDifferenceBoolean)
                                {
                                    ConfigurationDifference configDifference = fillConfigurationDifference(
                                        jobTargetReference,
                                        jobTargetDifference,
                                        configEntityReference,
                                        DIFFERENCE_DIFFERENT,
                                        prop.Name,
                                        propValueReferenceBoolean.ToString(),
                                        propValueDifferenceBoolean.ToString());

                                    configurationDifferencesList.Add(configDifference);
                                }
                            }

                            break;

                        case FieldComparisonType.SemicolonMultiLineValueComparison:
                            // These are strings separated by ;\n
                            string propValueReferenceMultiLineString = (string)propValueReferenceObject;
                            string propValueDifferenceMultiLineString = (string)propValueDifferenceObject;

                            configurationDifferencesList.AddRange(compareTwoEntitiesMultiValueStringProperty(jobTargetReference, jobTargetDifference, configEntityReference, configEntityReference, prop.Name, propValueReferenceMultiLineString, propValueDifferenceMultiLineString));

                            break;

                        case FieldComparisonType.XmlValueComparison:
                            // Compare XML tree
                            string propValueReferenceXML = (string)propValueReferenceObject;
                            string propValueDifferenceXML = (string)propValueDifferenceObject;

                            configurationDifferencesList.AddRange(compareTwoEntitiesXMLProperty(jobTargetReference, jobTargetDifference, configEntityReference, configEntityReference, prop.Name, propValueReferenceXML, propValueDifferenceXML));

                            break;

                        case FieldComparisonType.JSONValueComparison:
                            // Compare JSON tree
                            string propValueReferenceJSON = (string)propValueReferenceObject;
                            string propValueDifferenceJSON = (string)propValueDifferenceObject;

                            configurationDifferencesList.AddRange(compareTwoEntitiesJSONProperty(jobTargetReference, jobTargetDifference, configEntityReference, configEntityReference, prop.Name, propValueReferenceJSON, propValueDifferenceJSON));

                            break;

                        default:
                            // Not sure what this value is, skip
                            logger.Warn("Unknown FieldComparisonAttribute.FieldComparison={0}", fieldComparisonAttribute.FieldComparison);

                            break;
                    }
                }
            }

            return configurationDifferencesList;
        }

        private List<ConfigurationDifference> compareTwoEntitiesMultiValueStringProperty(
            JobTarget jobTargetReference,
            JobTarget jobTargetDifference,
            ConfigurationEntityBase configEntityReference,
            ConfigurationEntityBase configEntityDifference,
            string propertyName,
            string propValueReferenceMultiLineString,
            string propValueDifferenceMultiLineString)
        {
            List<ConfigurationDifference> configurationDifferencesList = new List<ConfigurationDifference>(16);

            List<System.String> propValueMultiLineListReference = propValueReferenceMultiLineString.Split(new string[] { ";\n" }, StringSplitOptions.RemoveEmptyEntries).ToList<System.String>();
            propValueMultiLineListReference.Sort();

            List<System.String> propValueMultiLineListDifference = propValueDifferenceMultiLineString.Split(new string[] { ";\n" }, StringSplitOptions.RemoveEmptyEntries).ToList<System.String>();
            propValueMultiLineListDifference.Sort();

            while (propValueMultiLineListReference.Count > 0)
            {
                string propValueReferenceString = propValueMultiLineListReference[0];
                propValueMultiLineListReference.RemoveAt(0);

                // Find match for this configuration entity
                int configEntityDifferenceIndex = propValueMultiLineListDifference.FindIndex(c => String.Compare(c, propValueReferenceString, true) == 0);
                if (configEntityDifferenceIndex < 0)
                {
                    // No match. This must be entity BBB, where item in Difference is MISSING
                    ConfigurationDifference configDifference = fillConfigurationDifference(
                        jobTargetReference,
                        jobTargetDifference,
                        configEntityReference,
                        DIFFERENCE_MISSING,
                        propertyName,
                        propValueReferenceString,
                        String.Empty);

                    configurationDifferencesList.Add(configDifference);
                }
                else
                {
                    // Found matching entity AAA. No need to compare them, by definition they are matching 
                    string propValueDifferenceString = propValueMultiLineListDifference[configEntityDifferenceIndex];
                    propValueMultiLineListDifference.RemoveAt(configEntityDifferenceIndex);
                }
            }

            // Whatever is left in Difference list must be like entity CCC, therefore they are all EXTRA
            if (propValueMultiLineListDifference.Count > 0)
            {
                for (int i = 0; i < propValueMultiLineListDifference.Count; i++)
                {
                    string propValueDifferenceString = propValueMultiLineListDifference[i];

                    ConfigurationDifference configDifference = fillConfigurationDifference(
                        jobTargetReference,
                        jobTargetDifference,
                        configEntityDifference,
                        DIFFERENCE_EXTRA,
                        propertyName,
                        String.Empty,
                        propValueDifferenceString);

                    configurationDifferencesList.Add(configDifference);
                }
            }
            return configurationDifferencesList;
        }

        private List<ConfigurationDifference> compareTwoEntitiesJSONProperty(
            JobTarget jobTargetReference,
            JobTarget jobTargetDifference,
            ConfigurationEntityBase configEntityReference,
            ConfigurationEntityBase configEntityDifference,
            string propertyName,
            string propValueReferenceJSON,
            string propValueDifferenceJSON)
        {
            List<ConfigurationDifference> configurationDifferencesList = new List<ConfigurationDifference>(16);

            // Do a spot check on the strings to see if they are different at all
            if (String.Compare(propValueReferenceJSON, propValueDifferenceJSON, false) == 0)
            {
                // The strings are identical, no need to bother with anything
                return configurationDifferencesList;
            }

            // If hit here, the strings are different. Let's parse them into JSON and compare element by element
            JToken propValueReference = null;
            JToken propValueDifference = null;
            try
            {
                if (propValueReferenceJSON.Length > 0)
                {
                    propValueReference = JToken.Parse(propValueReferenceJSON);
                }
                if (propValueDifferenceJSON.Length > 0)
                {
                    propValueDifference = JToken.Parse(propValueDifferenceJSON);
                }
            }
            catch (JsonReaderException ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);
            }

            // Check for success in parsing object from string
            if (propValueReference == null && propValueDifference == null)
            {
                return configurationDifferencesList;
            }
            else if (propValueReference != null && propValueDifference == null)
            {
                ConfigurationDifference configDifference = fillConfigurationDifference(
                    jobTargetReference,
                    jobTargetDifference,
                    configEntityReference,
                    DIFFERENCE_MISSING,
                    propertyName,
                    propValueReferenceJSON,
                    String.Empty);

                configurationDifferencesList.Add(configDifference);

                return configurationDifferencesList;
            }
            else if (propValueReference == null && propValueDifference != null)
            {
                ConfigurationDifference configDifference = fillConfigurationDifference(
                    jobTargetReference,
                    jobTargetDifference,
                    configEntityDifference,
                    DIFFERENCE_EXTRA,
                    propertyName,
                    String.Empty,
                    propValueDifferenceJSON);

                configurationDifferencesList.Add(configDifference);

                return configurationDifferencesList;
            }

            // If hit this line, we have two perfectly good JSON objects that contain slightly different JSON content
            // Let's get all their properties and look through them each
            Dictionary<string, string> tokenListReference = new Dictionary<string, string>(1024);
            flattenJSONObjectIntoDictionary(propValueReference, tokenListReference);

            Dictionary<string, string> tokenListDifference = new Dictionary<string, string>(1024);
            flattenJSONObjectIntoDictionary(propValueDifference, tokenListDifference);

            while (tokenListReference.Count > 0)
            {
                KeyValuePair<string, string> referenceKVP = tokenListReference.First();
                tokenListReference.Remove(referenceKVP.Key);

                if (tokenListDifference.ContainsKey(referenceKVP.Key) == false)
                {
                    // No match. This must be entity BBB, where item in Difference is MISSING
                    ConfigurationDifference configDifference = fillConfigurationDifference(
                        jobTargetReference,
                        jobTargetDifference,
                        configEntityReference,
                        DIFFERENCE_MISSING,
                        String.Format("{0}/{1}", propertyName, referenceKVP.Key),
                        referenceKVP.Value,
                        String.Empty);

                    configurationDifferencesList.Add(configDifference);
                }
                else
                {
                    // Found matching entity AAA. Let's compare them against each other
                    string propValueReferenceString = referenceKVP.Value;
                    string propValueDifferenceString = tokenListDifference[referenceKVP.Key];
                    tokenListDifference.Remove(referenceKVP.Key);

                    if (String.Compare(propValueReferenceString, propValueDifferenceString, false) != 0)
                    {
                        ConfigurationDifference configDifference = fillConfigurationDifference(
                            jobTargetReference,
                            jobTargetDifference,
                            configEntityReference,
                            DIFFERENCE_DIFFERENT,
                            String.Format("{0}/{1}", propertyName, referenceKVP.Key),
                            propValueReferenceString,
                            propValueDifferenceString);

                        configurationDifferencesList.Add(configDifference);
                    }
                }
            }

            // Whatever is left in Difference list must be like entity CCC, therefore they are all EXTRA
            if (tokenListDifference.Count > 0)
            {
                foreach (KeyValuePair<string, string> differenceKVP in tokenListDifference)
                {
                    ConfigurationDifference configDifference = fillConfigurationDifference(
                        jobTargetReference,
                        jobTargetDifference,
                        configEntityDifference,
                        DIFFERENCE_EXTRA,
                        String.Format("{0}/{1}", propertyName, differenceKVP.Key),
                        String.Empty,
                        differenceKVP.Value);

                    configurationDifferencesList.Add(configDifference);
                }
            }

            return configurationDifferencesList;
        }

        private List<ConfigurationDifference> compareTwoEntitiesXMLProperty(
            JobTarget jobTargetReference,
            JobTarget jobTargetDifference,
            ConfigurationEntityBase configEntityReference,
            ConfigurationEntityBase configEntityDifference,
            string propertyName,
            string propValueReferenceXML,
            string propValueDifferenceXML)
        {
            List<ConfigurationDifference> configurationDifferencesList = new List<ConfigurationDifference>(16);

            // Do a spot check on the strings to see if they are different at all
            if (String.Compare(propValueReferenceXML, propValueDifferenceXML, false) == 0)
            {
                // The strings are identical, no need to bother with anything
                return configurationDifferencesList;
            }

            // If hit here, the strings are different. Let's parse them into XML and compare element by element
            XmlDocument propValueReference = null;
            XmlDocument propValueDifference = null;
            try
            {
                if (propValueReferenceXML.Length > 0)
                {
                    propValueReference = new XmlDocument();
                    propValueReference.LoadXml(propValueReferenceXML);
                }

                if (propValueDifferenceXML.Length > 0)
                {
                    propValueDifference = new XmlDocument();
                    propValueDifference.LoadXml(propValueDifferenceXML);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                loggerConsole.Error(ex);
            }

            // Check for success in parsing object from string
            if (propValueReference == null && propValueDifference == null)
            {
                return configurationDifferencesList;
            }
            else if (propValueReference != null && propValueDifference == null)
            {
                ConfigurationDifference configDifference = fillConfigurationDifference(
                    jobTargetReference,
                    jobTargetDifference,
                    configEntityReference,
                    DIFFERENCE_MISSING,
                    propertyName,
                    propValueReferenceXML,
                    String.Empty);

                configurationDifferencesList.Add(configDifference);

                return configurationDifferencesList;
            }
            else if (propValueReference == null && propValueDifference != null)
            {
                ConfigurationDifference configDifference = fillConfigurationDifference(
                    jobTargetReference,
                    jobTargetDifference,
                    configEntityDifference,
                    DIFFERENCE_EXTRA,
                    propertyName,
                    String.Empty,
                    propValueDifferenceXML);

                configurationDifferencesList.Add(configDifference);

                return configurationDifferencesList;
            }

            // If hit this line, we have two perfectly good XML objects that contain slightly different XML content
            // Let's get all their properties and look through them each
            Dictionary<string, string> tokenListReference = new Dictionary<string, string>(1024);
            flattenXMLObjectIntoDictionary(propValueReference.DocumentElement, "", tokenListReference);

            Dictionary<string, string> tokenListDifference = new Dictionary<string, string>(1024);
            flattenXMLObjectIntoDictionary(propValueDifference.DocumentElement, "", tokenListDifference);

            while (tokenListReference.Count > 0)
            {
                KeyValuePair<string, string> referenceKVP = tokenListReference.First();
                tokenListReference.Remove(referenceKVP.Key);

                if (tokenListDifference.ContainsKey(referenceKVP.Key) == false)
                {
                    // No match. This must be entity BBB, where item in Difference is MISSING
                    ConfigurationDifference configDifference = fillConfigurationDifference(
                        jobTargetReference,
                        jobTargetDifference,
                        configEntityReference,
                        DIFFERENCE_MISSING,
                        String.Format("{0}/{1}", propertyName, referenceKVP.Key),
                        referenceKVP.Value,
                        String.Empty);

                    configurationDifferencesList.Add(configDifference);
                }
                else
                {
                    // Found matching entity AAA. Let's compare them against each other
                    string propValueReferenceString = referenceKVP.Value;
                    string propValueDifferenceString = tokenListDifference[referenceKVP.Key];
                    tokenListDifference.Remove(referenceKVP.Key);

                    if (String.Compare(propValueReferenceString, propValueDifferenceString, false) != 0)
                    {
                        ConfigurationDifference configDifference = fillConfigurationDifference(
                            jobTargetReference,
                            jobTargetDifference,
                            configEntityReference,
                            DIFFERENCE_DIFFERENT,
                            String.Format("{0}/{1}", propertyName, referenceKVP.Key),
                            propValueReferenceString,
                            propValueDifferenceString);

                        configurationDifferencesList.Add(configDifference);
                    }
                }
            }

            // Whatever is left in Difference list must be like entity CCC, therefore they are all EXTRA
            if (tokenListDifference.Count > 0)
            {
                foreach (KeyValuePair<string, string> differenceKVP in tokenListDifference)
                {
                    ConfigurationDifference configDifference = fillConfigurationDifference(
                        jobTargetReference,
                        jobTargetDifference,
                        configEntityDifference,
                        DIFFERENCE_EXTRA,
                        String.Format("{0}/{1}", propertyName, differenceKVP.Key),
                        String.Empty,
                        differenceKVP.Value);

                    configurationDifferencesList.Add(configDifference);
                }
            }

            return configurationDifferencesList;
        }

        private void flattenJSONObjectIntoDictionary(JToken jTokenRoot, Dictionary<string, string> dictionary)
        {
            // Recursively go through each of the tokens
            foreach (JToken jToken in jTokenRoot)
            {
                if (jToken.Type == JTokenType.Object)
                {
                    // Object
                    // "{ "test1": 123, "test2: 321 }"
                    flattenJSONObjectIntoDictionary(jToken, dictionary);
                }
                else if (jToken.Type == JTokenType.Array)
                {
                    // Array of something
                    // [{ "test1": 123, "test2: 321 }, { "test1": 123, "test2: 321 }]
                    foreach (JToken jTokenArrayMember in jToken)
                    {
                        flattenJSONObjectIntoDictionary(jTokenArrayMember, dictionary);
                    }
                }
                else if (jToken.Type == JTokenType.Property)
                {
                    JToken jTokenProperty = ((JProperty)jToken).Value;

                    if (jTokenProperty is JObject)
                    {
                        if (jTokenProperty.HasValues == false)
                        {
                            // Empty object
                            // "test1" : {} 
                            dictionary.Add(jTokenProperty.Path, jTokenProperty.ToString());
                        }
                        else
                        {
                            // Object
                            // "test1" : { "test1": 123, "test2: 321 }
                            flattenJSONObjectIntoDictionary(jTokenProperty, dictionary);
                        }
                    }
                    else if (jTokenProperty is JArray)
                    {
                        if (jTokenProperty.HasValues == false)
                        {
                            // Empty array
                            // "test1" : [] 
                            dictionary.Add(jTokenProperty.Path, jTokenProperty.ToString());
                        }
                        else
                        {
                            int i = 0;
                            foreach (JToken jTokenArrayMember in jTokenProperty)
                            {
                                if (jTokenArrayMember.Type == JTokenType.Object || jTokenArrayMember.Type == JTokenType.Array)
                                {
                                    // Array of objects
                                    // "test1" : [{ "test1": 123, "test2: 321 }, { "test1": 123, "test2: 321 }]
                                    flattenJSONObjectIntoDictionary(jTokenArrayMember, dictionary);
                                }
                                else
                                {
                                    // Array of values
                                    // "test1" : ["test1", "test2", "test3"]
                                    dictionary.Add(String.Format("{0}/{1}", jTokenProperty.Path, i), jTokenArrayMember.ToString());
                                }
                                i++;
                            }
                        }
                    }
                    else
                    {
                        // Value
                        // "test1" : "value1"
                        dictionary.Add(jToken.Path, jTokenProperty.ToString());
                    }
                }
                else
                {
                    // Value
                    // "test1" : "value1"
                    dictionary.Add(jToken.Path, jToken.ToString());
                }
            }
        }

        private void flattenXMLObjectIntoDictionary(XmlNode xmlNodeRoot, string relativePath, Dictionary<string, string> dictionary)
        {
            if (xmlNodeRoot.ChildNodes != null && xmlNodeRoot.ChildNodes.Count > 0)
            {
                for (int i = 0; i < xmlNodeRoot.ChildNodes.Count; i++)
                {
                    XmlNode xmlNode = xmlNodeRoot.ChildNodes[i];

                    if (xmlNode.Attributes != null)
                    {
                        foreach (XmlAttribute xmlAttribute in xmlNode.Attributes)
                        {
                            dictionary.Add(String.Format("{0}{1}:{2}[{3}]/", relativePath, xmlNode.Name, i, xmlAttribute.Name), xmlAttribute.InnerText);
                        }
                    }

                    if (xmlNode.Value != null)
                    {
                        if (xmlNode.Name == "#text")
                        {
                            dictionary.Add(relativePath, xmlNode.Value);
                        }
                        else
                        {
                            dictionary.Add(String.Format("{0}{1}:{2}/", relativePath, xmlNode.Name, i), xmlNode.Value);
                        }
                    }
                    if (xmlNode.ChildNodes.Count > 0)
                    {
                        flattenXMLObjectIntoDictionary(xmlNode, String.Format("{0}{1}:{2}/", relativePath, xmlNode.Name, i), dictionary);
                    }
                }
            }
            else
            {
                XmlNode xmlNode = xmlNodeRoot;

                if (xmlNode.Attributes != null)
                {
                    foreach (XmlAttribute xmlAttribute in xmlNode.Attributes)
                    {
                        dictionary.Add(String.Format("{0}{1}:{2}[{3}]/", relativePath, xmlNode.Name, 0, xmlAttribute.Name), xmlAttribute.InnerText);
                    }
                }

                if (xmlNode.Value != null)
                {
                    if (xmlNode.Name == "#text")
                    {
                        dictionary.Add(relativePath, xmlNode.Value);
                    }
                    else
                    {
                        dictionary.Add(String.Format("{0}{1}:{2}/", relativePath, xmlNode.Name, 0), xmlNode.Value);
                    }
                }
            }
        }

        private ConfigurationDifference fillConfigurationDifference(
            JobTarget jobTargetReference,
            JobTarget jobTargetDifference,
            ConfigurationEntityBase configEntityBeingCompared,
            string differenceType,
            string propertyName,
            string referenceValue,
            string differenceValue
            )
        {
            ConfigurationDifference configDifference = new ConfigurationDifference();

            configDifference.ReferenceConroller = jobTargetReference.Controller;
            configDifference.ReferenceApp = jobTargetReference.Application;
            configDifference.DifferenceController = jobTargetDifference.Controller;
            configDifference.DifferenceApp = jobTargetDifference.Application;
            configDifference.Difference = differenceType;

            configDifference.EntityName = configEntityBeingCompared.EntityName;
            configDifference.RuleType = configEntityBeingCompared.RuleType;
            configDifference.RuleSubType = configEntityBeingCompared.RuleSubType;
            configDifference.TierName = configEntityBeingCompared.TierName;
            configDifference.EntityIdentifier = configEntityBeingCompared.EntityIdentifier;

            configDifference.Property = propertyName;
            configDifference.ReferenceValue = referenceValue;
            configDifference.DifferenceValue = differenceValue;

            return configDifference;
        }
    }
    
}

