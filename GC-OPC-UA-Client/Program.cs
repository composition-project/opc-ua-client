using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Mono.Options;
using System.Linq;

namespace GC_OPC_UA_Client
{
    public enum ExitCode : int
    {
        Ok = 0,
        ErrorCreateApplication = 0x11,
        ErrorDiscoverEndpoints = 0x12,
        ErrorCreateSession = 0x13,
        ErrorBrowseNamespace = 0x14,
        ErrorCreateSubscription = 0x15,
        ErrorMonitoredItem = 0x16,
        ErrorAddSubscription = 0x17,
        ErrorRunning = 0x18,
        ErrorNoKeepAlive = 0x30,
        ErrorInvalidCommandLine = 0x100
    };
    public class Program
    {
        static int Main(string[] args)
        {
           LogHandler.WriteLogFile("GrainCloud OPC-UA Client started");

            // command line options
            bool showHelp = false;
            int stopTimeout = Timeout.Infinite;
            bool autoAccept = true;

            Mono.Options.OptionSet options = new Mono.Options.OptionSet {
                { "h|help", "show this message and exit", h => showHelp = h != null },
                { "a|autoaccept", "auto accept certificates (for testing only)", a => autoAccept = a != null },
                { "t|timeout=", "the number of seconds until the client stops.", (int t) => stopTimeout = t }
            };

            IList<string> extraArgs = null;
            try
            {
                extraArgs = options.Parse(args);
                if (extraArgs.Count > 1)
                {
                    foreach (string extraArg in extraArgs)
                    {
                       LogHandler.WriteLogFile("Error: Unknown option: "+ extraArg);
                        showHelp = true;
                    }
                }
            }
            catch (OptionException e)
            {
               LogHandler.WriteLogFile(e.Message);
                showHelp = true;
            }

            if (showHelp)
            {
                // show some app description message
               LogHandler.WriteLogFile(Utils.IsRunningOnMono() ?
                    "Usage: mono MonoConsoleClient.exe [OPTIONS] [ENDPOINTURL]" :
                    "Usage: dotnet NetCoreConsoleClient.dll [OPTIONS] [ENDPOINTURL]");
               

                // output the options
               LogHandler.WriteLogFile("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return (int)ExitCode.ErrorInvalidCommandLine;
            }

            

            IConfiguration config = new ConfigurationBuilder()
          .AddJsonFile("appsettings.json", true, true)
          .Build();

            // Read configuration
            settings.OPCUAServerAddress = config["OPCUAServerAddress"];
            settings.CloudLogFolder = config["CloudLogFolder"];
            settings.DataBaseFileAndPath = config["DataBaseFileAndPath"];
            settings.PlantRefId = config["PlantRefId"];
            settings.AddIDToTagName = bool.Parse(config["AddIDToTagName"]);
            settings.UseRPiTime = bool.Parse(config["UseRPiTime"]);
            settings.IgnoreTags = config
             .GetSection("IgnoreTags")
              .GetChildren()
                .Select(x => x.Value)
                .ToArray();

            //CleanDB db = new CleanDB();
            //db.CleanCrap();
            OpcUaClient client = new OpcUaClient(settings.OPCUAServerAddress, autoAccept, stopTimeout);
            client.Run();
            LogHandler.WriteLogFile("Process exit" + ((int)OpcUaClient.ExitCode).ToString());
            return (int)OpcUaClient.ExitCode;
        }
    }



    public class OpcUaClient
    {
        const int ReconnectPeriod = 10;
        Session session;
        SessionReconnectHandler reconnectHandler;
        string endpointURL;
        int clientRunTime = Timeout.Infinite;
        static bool autoAccept = false;
        static ExitCode exitCode;
        List<Subscription> sublist = new List<Subscription>();
        ReferenceDescriptionCollection attributes = new ReferenceDescriptionCollection();

        public OpcUaClient(string _endpointURL, bool _autoAccept, int _stopTimeout)
        {
            endpointURL = _endpointURL;
            autoAccept = _autoAccept;
            clientRunTime = _stopTimeout <= 0 ? Timeout.Infinite : _stopTimeout * 1000;
            CreateVariableChangeTableIfNotExists();
        }

        public void Run()
        {
            try
            {
                UAClient().Wait();
            }
            catch (Exception ex)
            {
                Utils.Trace("ServiceResultException:" + ex.Message);
               LogHandler.WriteLogFile("Exception: " + ex.Message);
                return;
            }

            ManualResetEvent quitEvent = new ManualResetEvent(false);
            try
            {
                Console.CancelKeyPress += (sender, eArgs) =>
                {
                    quitEvent.Set();
                    eArgs.Cancel = true;
                };
            }
            catch
            {
            }

            // wait for timeout or Ctrl-C
            quitEvent.WaitOne(clientRunTime);
            foreach (var sub in sublist)
                session.RemoveSubscription(sub);
            // return error conditions
            if (session.KeepAliveStopped)
            {
                exitCode = ExitCode.ErrorNoKeepAlive;
                return;
            }
            session.Close();

            exitCode = ExitCode.Ok;
        }

        public static ExitCode ExitCode { get => exitCode; }

        private async Task UAClient()
        {
           LogHandler.WriteLogFile("1 - Create an Application Configuration.");
            exitCode = ExitCode.ErrorCreateApplication;

            ApplicationInstance application = new ApplicationInstance
            {
                ApplicationName = "UA Core Sample Client",
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = Utils.IsRunningOnMono() ? "Opc.Ua.MonoSampleClient" : "Opc.Ua.SampleClient"
            };

            // load the application configuration.
            ApplicationConfiguration config = await application.LoadApplicationConfiguration(false);

            // check the application certificate.
            bool haveAppCertificate = await application.CheckApplicationInstanceCertificate(false, 0);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            if (haveAppCertificate)
            {
                config.ApplicationUri = Utils.GetApplicationUriFromCertificate(config.SecurityConfiguration.ApplicationCertificate.Certificate);
                if (config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
                {
                    autoAccept = true;
                }
                config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }
            else
            {
               LogHandler.WriteLogFile("    WARN: missing application certificate, using unsecure connection.");
            }

           LogHandler.WriteLogFile("2 - Discover endpoints of " + endpointURL);
            exitCode = ExitCode.ErrorDiscoverEndpoints;
            var selectedEndpoint = CoreClientUtils.SelectEndpoint(endpointURL, false, 15000);
           LogHandler.WriteLogFile("    Selected endpoint uses: " + 
                selectedEndpoint.SecurityPolicyUri.Substring(selectedEndpoint.SecurityPolicyUri.LastIndexOf('#') + 1));

            LogHandler.WriteLogFile("3 - Create a session with OPC UA server.");
            exitCode = ExitCode.ErrorCreateSession;
            var endpointConfiguration = EndpointConfiguration.Create(config);
            var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
            session = await Session.Create(config, endpoint, false, "OPC UA Console Client", 60000, new UserIdentity(new AnonymousIdentityToken()), null);

            // register keep alive handler
            session.KeepAlive += Client_KeepAlive;

           LogHandler.WriteLogFile("4 - Browse the OPC UA server namespace.");
            exitCode = ExitCode.ErrorBrowseNamespace;
            ReferenceDescriptionCollection references;
            Byte[] continuationPoint;

            references = session.FetchReferences(ObjectIds.ObjectsFolder);


            session.Browse(
                null,
                null,
                ObjectIds.ObjectsFolder,
                40000u,
                BrowseDirection.Forward,
                ReferenceTypeIds.HierarchicalReferences,
                true,
                (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method,
                out continuationPoint,
                out references);

           LogHandler.WriteLogFile(" DisplayName, BrowseName, NodeClass");
            foreach (var rd in references)
            {
                LogHandler.WriteLogFile("root" + ":" + rd.DisplayName + "; " + rd.BrowseName + "; " + rd.NodeClass + "; " + rd.NodeId);
                ListAll(rd, "root");
            }

           LogHandler.WriteLogFile("5 - Create a subscription with publishing interval of 1 second.");
            exitCode = ExitCode.ErrorCreateSubscription;
            Subscription subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = 1000 };

           LogHandler.WriteLogFile("6 - Add a list of items (server current time and status) to the subscription.");
            exitCode = ExitCode.ErrorMonitoredItem;
            var list = new List<MonitoredItem> {
                new MonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = "ServerStatusCurrentTime", StartNodeId = "i="+Variables.Server_ServerStatus_CurrentTime.ToString()
                }
            };
            int i = 1;
            foreach (var rd in attributes)
            {
                list.Add(new MonitoredItem()
                {
                    DisplayName = rd.BrowseName.ToString(),
                    StartNodeId = rd.NodeId.ToString()
                });
                i++;
                if (i % 100 == 0)
                {
                    list.ForEach(j => j.Notification += OnNotification);
                    subscription.AddItems(list);
                    sublist.Add(subscription);
                    subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = 1000 };
                    list = new List<MonitoredItem>();
                }
            }
            list.ForEach(ji => ji.Notification += OnNotification);
            subscription.AddItems(list);
            sublist.Add(subscription);

            LogHandler.WriteLogFile("7 - Add the subscription to the session.");
            exitCode = ExitCode.ErrorAddSubscription;
            foreach (Subscription sub in sublist)
            {
                session.AddSubscription(sub);
                sub.Create();
            }

           LogHandler.WriteLogFile("8 - Running...Press Ctrl-C to exit...");
            exitCode = ExitCode.ErrorRunning;
        }

        private void Client_KeepAlive(Session sender, KeepAliveEventArgs e)
        {
            if (e.Status != null && ServiceResult.IsNotGood(e.Status))
            {
               LogHandler.WriteLogFile( e.Status + " " + sender.OutstandingRequestCount.ToString() + "/" + sender.DefunctRequestCount.ToString());

                if (reconnectHandler == null)
                {
                   LogHandler.WriteLogFile("--- RECONNECTING ---");
                    reconnectHandler = new SessionReconnectHandler();
                    reconnectHandler.BeginReconnect(sender, ReconnectPeriod * 1000, Client_ReconnectComplete);
                }
            }
        }

        private void ListAll(ReferenceDescription rd, string fatherName)
        {
            ReferenceDescriptionCollection tempRefs = new ReferenceDescriptionCollection();
           
            if (rd.BrowseName == "Server") return;
            if (rd.NodeClass == NodeClass.Variable && !settings.IgnoreTags.Contains(rd.BrowseName.ToString())) 
            {
                LogHandler.WriteLogFile("Subs:"+fatherName + ":" + rd.DisplayName + "; " + rd.BrowseName.ToString() + "; " + rd.NodeClass + "; " + rd.NodeId);
                attributes.Add(rd);
            }
            else if (settings.IgnoreTags.Contains(rd.BrowseName.ToString()))
            {
                LogHandler.WriteLogFile("Ignored tag: Subs:" + fatherName + ":" + rd.DisplayName + "; " + rd.BrowseName + "; " + rd.NodeClass + "; " + rd.NodeId);
            }
            ReferenceDescriptionCollection nextRefs;
            byte[] nextCp;
            bool moreData = true;
            session.Browse(
                null,
                null,
                ExpandedNodeId.ToNodeId(rd.NodeId, session.NamespaceUris),
                40000u,
                BrowseDirection.Forward,
                ReferenceTypeIds.HierarchicalReferences,
                true,
                (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method,
                out nextCp,
                out nextRefs);
            
            while (moreData)
            {
                tempRefs.AddRange(nextRefs);

                if (nextCp == null)
                    moreData = false;
                else
                {
                    byte[] tmpCp;
                    session.BrowseNext(null, false, nextCp, out tmpCp, out nextRefs);
                    nextCp = tmpCp;
                    
                }
            }
            foreach (var nextRd in tempRefs)
            {
                ListAll(nextRd, rd.DisplayName.Text);
            }

        }

        private void Client_ReconnectComplete(object sender, EventArgs e)
        {
            // ignore callbacks from discarded objects.
            if (!Object.ReferenceEquals(sender, reconnectHandler))
            {
                return;
            }

            session = reconnectHandler.Session;
            reconnectHandler.Dispose();
            reconnectHandler = null;

           LogHandler.WriteLogFile("--- RECONNECTED ---");
        }

        private static void OnNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
            MonitoredItemNotification datachange = e.NotificationValue as MonitoredItemNotification;

            if (datachange == null)
            {
                System.Console.WriteLine("oops");
                return;
            }
            else if (item.DisplayName != "ServerStatusCurrentTime" && datachange.Value.WrappedValue.ToString() != "(null)"  && !settings.IgnoreTags.Contains(item.DisplayName))
            {
                string tagName = item.DisplayName;
                if (settings.AddIDToTagName) 
                    tagName = item.StartNodeId + ":" + tagName;
                string ts = datachange.Value.SourceTimestamp.ToString("yyyy-MM-ddTHH:mm:ss.fff");
                if (settings.UseRPiTime)
                {
                    ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff");
                }
                SaveToDB(settings.PlantRefId, ts, tagName, datachange.Value.WrappedValue.ToString());
            }



          

        }


        static public void SaveToDB(string PlantID, string Time, string Tag, string value)
        {



            // CreateVariableChangeTableIfNotExists();
            using (var cnn = new SqliteConnection("Data Source=" + settings.DataBaseFileAndPath + ";Cache=Shared"))
            {
                try
                {
                    cnn.Open();

                    StringBuilder sb = new StringBuilder();

                    sb.Append(@"INSERT INTO PLCTagChanged ([PlantID],[Time], [Tag], [value], [processed]) 
                                    VALUES(@PlantID,@Time,@Tag,@value,@processed); ");


                    SqliteCommand cmd = cnn.CreateCommand();
                    cmd.CommandText = sb.ToString();
                    AddParameter(ref cmd, "@PlantID", DbType.String, PlantID);
                    AddParameter(ref cmd, "@Time", DbType.String, Time);
                    AddParameter(ref cmd, "@Tag", DbType.String, Tag);
                    AddParameter(ref cmd, "@value", DbType.String, value);
                    AddParameter(ref cmd, "@processed", DbType.Boolean, false);

                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    LogHandler.WriteLogFile("SaveToDB failed:" + ex.Message);
                }

            }
        }

        /// <summary>
        /// Adds a parameter to supplied dbcommand
        /// </summary>
        /// <param name="dbcommand">DbCommand object that the parameter will be added to</param>
        /// <param name="paramenterName">Name of the parameter to be added</param>
        /// <param name="dbType">The database datatype of the parameter</param>
        /// <param name="value">The actual value</param>
        static void AddParameter(ref SqliteCommand dbcommand, string paramenterName, DbType dbType, object value)
        {
            DbParameter keyParam = dbcommand.CreateParameter();
            keyParam.ParameterName = paramenterName;
            keyParam.DbType = dbType;
            keyParam.Value = value;
            dbcommand.Parameters.Add(keyParam);
        }


        static void CreateVariableChangeTableIfNotExists()
        {



            using (var cnn = new SqliteConnection("Data Source=" + settings.DataBaseFileAndPath))
            {
                cnn.Open();
                using (SqliteCommand cmd = cnn.CreateCommand())
                {
                    cmd.CommandText =
                    @"SELECT count(name) FROM sqlite_master WHERE type='table' AND tbl_name='PLCTagChanged';";
                    int noTables = Convert.ToInt32(cmd.ExecuteScalar());
                    if (0 == noTables)
                    {
                        try
                        {
                            cmd.CommandText = @" CREATE TABLE PLCTagChanged (
                        PlantID nvarchar(500),
                           Time nvarchar(500)
                        , Tag nvarchar(100)
                        , value nvarchar(100)
                        , processed boolean
                        , id integer primary key  autoincrement);";

                            cmd.ExecuteNonQuery();
                        }
                        catch (Exception e)
                        {
                            LogHandler.WriteLogFile("CreateVariableChangeTableIfNotExists Exception:" + e.Message);
                      
                        }
                    }
                }
            }
        }
        private static void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = autoAccept;
                if (autoAccept)
                {
                   LogHandler.WriteLogFile("Accepted Certificate: " + e.Certificate.Subject);
                }
                else
                {
                   LogHandler.WriteLogFile("Rejected Certificate: " +  e.Certificate.Subject);
                }
            }
        }

    }
}
