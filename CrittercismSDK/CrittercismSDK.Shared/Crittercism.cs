using CrittercismSDK;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
#if NETFX_CORE
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Networking.Connectivity;
#elif WINDOWS_PHONE
using Microsoft.Phone.Info;
using Microsoft.Phone.Shell;
using Microsoft.Phone.Net.NetworkInformation;
#else
using Microsoft.Win32;
using System.Reflection;
#endif // NETFX_CORE

namespace CrittercismSDK {
    /// <summary>
    /// Crittercism.
    /// </summary>
    public class Crittercism {
        #region Constants

        private const string errorNotInitialized="ERROR: Crittercism not initialized yet.";

        #endregion Constants

        #region Properties

        /// <summary>
        /// The auto run queue reader
        /// </summary>
        internal static bool _autoRunQueueReader = true;

        /// <summary>
        /// The enable communication layer
        /// </summary>
        internal static bool _enableCommunicationLayer = true;

        /// <summary>
        /// The enable raise exception in communication layer
        /// </summary>
        internal static bool _enableRaiseExceptionInCommunicationLayer = false;

        internal static string AppVersion { get; private set; }
        internal static string DeviceId { get; private set; }
        internal static string DeviceModel { get; private set; }

        /// <summary>
        /// Gets or sets a queue of messages.
        /// </summary>
        /// <value> A SynchronizedQueue of messages. </value>
        internal static SynchronizedQueue<MessageReport> MessageQueue { get; set; }

        /// <summary>
        /// Gets or sets the current breadcrumbs.
        /// </summary>
        /// <value> The breadcrumbs. </value>
        internal static Breadcrumbs PrivateBreadcrumbs { get; set; }

        private static Breadcrumbs CurrentBreadcrumbs() {
            Breadcrumbs answer=PrivateBreadcrumbs.Copy();
            return answer;
        }

        internal static object lockObject=new Object();
        internal static volatile bool initialized=false;

        /// <summary>
        /// Gets or sets the identifier of the application.
        /// </summary>
        /// <value> The identifier of the application. </value>
        internal static string AppID { get; set; }

        internal static AppLocator appLocator { get; private set; }

        /// <summary>
        /// Gets or sets the operating system platform.
        /// </summary>
        /// <value> The operating system platform. </value>
        internal static string OSVersion="";

        /// <summary>
        /// Gets or sets the arbitrary user metadata.
        /// </summary>
        /// <value> The user metadata. </value>
        internal static Dictionary<string, string> Metadata { get; set; }

        private static Dictionary<string,string> CurrentMetadata() {
            Dictionary<string,string> answer=null;
            lock (lockObject) {
                answer=new Dictionary<string,string>(Metadata);
            }
            return answer;
        }

        /// <summary> 
        /// Message Counter
        /// </summary>
        internal static int messageCounter = 0;

        /// <summary> 
        /// The initial date
        /// </summary>
        internal static DateTime initialDate = DateTime.Now;

        /// <summary>
        /// The thread for the reader
        /// </summary>
#if NETFX_CORE
        internal static Task readerThread=null;
#else
        internal static Thread readerThread = null;
#endif

        /// <summary>
        /// AutoResetEvent for readerThread to observe
        /// </summary>
        internal static AutoResetEvent readerEvent = new AutoResetEvent(false);

        #endregion Properties

        #region OptOutStatus

        ////////////////////////////////////////////////////////////////
        // Developer's app only needs to OptOut once in it's life time.
        // To ever undo this, SetOptOutStatus(false) before calling
        // Init again.
        ////////////////////////////////////////////////////////////////

        // OptOut is internal for test cleanup, OW only 2 methods in
        // class Crittercism.cs should be touching member variable OptOut directly.
        internal static volatile bool OptOut=false;

        // Is OptOut known to be equal to what's persisted on disk?
        internal static volatile bool OptOutLoaded=false;

        private static string OptOutStatusPath="Crittercism\\OptOutStatus.js";
        private static void SaveOptOutStatus(bool optOutStatus) {
            // Knows how to persist value of OptOut
            StorageHelper.Save(Convert.ToBoolean(optOutStatus),OptOutStatusPath);
        }

        private static bool LoadOptOutStatus() {
            // Knows how to unpersist value of OptOut
            bool answer=false;
            if (StorageHelper.FileExists(OptOutStatusPath)) {
                answer=(bool)StorageHelper.Load(OptOutStatusPath,typeof(Boolean));
            };
            return answer;
        }

        public static bool GetOptOutStatus() {
            // Returns in memory cached value OptOut, getting it to be correct
            // value from persisted storage first, if necessary.
            if (!OptOutLoaded) {
                // Logic here to make sure OptOut is unpersisted correctly from
                // any possible previous session.  App is born with default OptOut
                // value equal to false (Crittercism enabled).
                lock (lockObject) {
                    // Check flag again inside lock in case our thread loses race.
                    if (!OptOutLoaded) {
                        OptOut=LoadOptOutStatus();
                        OptOutLoaded=true;
                    };
                };
            };
            return OptOut;
        }

        public static void SetOptOutStatus(bool optOut) {
            // Set in memory cached value OptOut, persisting if necessary.
            lock (lockObject) {
                // OptOut is volatile, but this method accesses it twice,
                // so we need the lock
                if (optOut!=GetOptOutStatus()) {
                    OptOut=optOut;
                    SaveOptOutStatus(optOut);
                }
            }
        }

        #endregion OptOutStatus

        #region Init

        private static string PrivateAppVersion() {
#if NETFX_CORE
            PackageVersion version=Package.Current.Id.Version;
            string answer=""+version.Major+"."+version.Minor+"."+version.Build+"."+version.Revision;
            Debug.WriteLine("PrivateAppVersion == "+answer);
            return answer;
#elif WINDOWS_PHONE
            return Application.Current.GetType().Assembly.GetName().Version.ToString();
#else
            // Should probably work in most cases.
            return Assembly.GetCallingAssembly().GetName().Version.ToString();
#endif
        }

        /// <summary>
        /// Retrieves the device id from storage.
        /// 
        /// If we don't have a device id, we create and store a new one.
        /// </summary>
        /// <returns>String with device_id, null otherwise</returns>
        private static string PrivateDeviceId() {
            string deviceId=null;
            string path=Path.Combine(StorageHelper.CrittercismPath(),"DeviceId.js");
            try {
                if (StorageHelper.FileExists(path)) {
                    deviceId=(string)StorageHelper.Load(path,typeof(String));
                }
            } catch (Exception e) {
                Crittercism.LogInternalException(e);
            }
            if (deviceId==null) {
                try {
                    deviceId=Guid.NewGuid().ToString();
                    StorageHelper.Save(deviceId,path);
                } catch (Exception e) {
                    Crittercism.LogInternalException(e);
                    // if deviceId==null is returned, then Crittercism should say
                    // it wasn't able to initialize
                }
            }
            Debug.WriteLine("LoadDeviceId --> "+deviceId);
            return deviceId;
        }

        private static string PrivateDeviceModel() {
            // TODO: We wish this method could be a lot better.
#if NETFX_CORE
#if WINDOWS_PHONE_APP
            return "Windows Phone";
#else
            return "Windows PC";
#endif // WINDOWS_PHONE_APP
#elif WINDOWS_PHONE
            return DeviceStatus.DeviceName;
#else
            return "Windows PC";
#endif // NETFX_CORE
        }
        
        internal static Dictionary<string,string> LoadMetadata() {
            Dictionary<string,string> answer=null;
            const string path="Metadata.js";
            if (StorageHelper.FileExists(path)) {
                answer=(Dictionary<string,string>)StorageHelper.Load(
                     path,
                     typeof(Dictionary<string,string>)
                 );
            }
            if (answer==null) {
                answer=new Dictionary<string,string>();
            }
            return answer;
        }

        private static string PrivateOSVersion() {
#if NETFX_CORE
            // TODO: Returning an empty string here makes us sad.
            // "You cannot get the OS or .NET framework version in a Windows Store app ...
            // Marked as answer by Anne Jing Microsoft contingent staff, Moderator"
            // https://social.msdn.microsoft.com/Forums/sqlserver/en-US/66e662a9-9ece-4863-8cf1-a5e259c7b571/c-windows-store-8-os-version-name-and-net-version-name
            string answer="";
#else
            string answer=Environment.OSVersion.Platform.ToString();
#endif
            return answer;
        }

        /// <summary>
        /// This method is invoked when the application starts or resume
        /// </summary>
        /// <param name="appID">    Identifier for the application. </param>
        private static void StartApplication(string appID) {
            // TODO: Why do we pass appID arg to this method?
            AppID=appID;
            OSVersion=PrivateOSVersion();
            PrivateBreadcrumbs=Breadcrumbs.SessionStart();
            MessageQueue=new SynchronizedQueue<MessageReport>(new Queue<MessageReport>());
            LoadQueue();
            CreateAppLoadReport();
        }

        /// <summary>
        /// Initialises Crittercism.
        /// </summary>
        /// <param name="appID">  Identifier for the application. </param>
        public static void Init(string appID) {
            try {
                if (GetOptOutStatus()) {
                    return;
                } else if (initialized) {
                    Debug.WriteLine("ERROR: Crittercism is already initialized");
                    return;
                };
                lock (lockObject) {
                    MessageReport.Init();
                    AppVersion=PrivateAppVersion();
                    DeviceId=PrivateDeviceId();
                    DeviceModel=PrivateDeviceModel();
                    Metadata=LoadMetadata();
                    appLocator=new AppLocator(appID);
                    QueueReader queueReader=new QueueReader(appLocator);
#if NETFX_CORE
                    Action threadStart=() => { queueReader.ReadQueue(); };
                    readerThread=new Task(threadStart);
#else
                    ThreadStart threadStart=new ThreadStart(queueReader.ReadQueue);
                    readerThread=new Thread(threadStart);
                    readerThread.Name="Crittercism";
#endif
                    // NOTE: Put initialized=true before readerThread.Start() .
                    // Later on, initialized may be reset back to false during shutdown,
                    // and readerThread will see initialized==false as a message to exit.
                    // Spares us from creating an additional "shuttingdown" flag.
                    initialized=true;
                    readerThread.Start();
                    // NOTE: Since StartApplication will induce an AppLoad, it seems
                    // best to put StartApplication after readerThread.Start() .
                    StartApplication(appID);
                    // _autoRunQueueReader for unit test purposes
                    if (_autoRunQueueReader&&_enableCommunicationLayer&&!(_enableRaiseExceptionInCommunicationLayer)) {
#if NETFX_CORE
                        Application.Current.UnhandledException+=Application_UnhandledException;
                        NetworkInformation.NetworkStatusChanged+=NetworkInformation_NetworkStatusChanged;
#elif WINDOWS_PHONE
                        Application.Current.UnhandledException+=new EventHandler<ApplicationUnhandledExceptionEventArgs>(SilverlightApplication_UnhandledException);
                        DeviceNetworkInformation.NetworkAvailabilityChanged+=DeviceNetworkInformation_NetworkAvailabilityChanged;
                        try {
                            if (PhoneApplicationService.Current!=null) {
                                PhoneApplicationService.Current.Activated+=new EventHandler<ActivatedEventArgs>(PhoneApplicationService_Activated);
                                PhoneApplicationService.Current.Deactivated+=new EventHandler<DeactivatedEventArgs>(PhoneApplicationService_Deactivated);
                            }
                        } catch (Exception e) {
                            Crittercism.LogInternalException(e);
                        }
#else
                        AppDomain.CurrentDomain.UnhandledException+=new UnhandledExceptionEventHandler(AppDomain_UnhandledException);
                        System.Windows.Forms.Application.ThreadException+=new ThreadExceptionEventHandler(WindowsFormsApplication_ThreadException);
#endif
                    }
                }
            } catch (Exception) {
                initialized=false;
            }
            if (initialized) {
                Debug.WriteLine("Crittercism initialized.");
            } else {
                Debug.WriteLine("Crittercism did not initialize.");
            }
        }
        #endregion Init

        #region ShutDown
        internal static void Save() {
            // Save current Crittercism state
            try {
                lock (lockObject) {
                    Debug.WriteLine("Save: SAVE STATE");
                    PrivateBreadcrumbs.Save();
                    foreach (MessageReport message in MessageQueue) {
                        message.Save();
                    }
                }
            } catch (Exception e) {
                LogInternalException(e);
            }
        }

        /// <summary>
        /// Shuts down Crittercism.
        /// </summary>
        public static void Shutdown() {
            // Shutdown Crittercism, including readerThread .
            Debug.WriteLine("Shutdown");
            if (initialized) {
                lock (lockObject) {
                    if (initialized) {
                        initialized=false;
                        // Get the readerThread to exit.
                        readerEvent.Set();
#if NETFX_CORE
                        readerThread.Wait();
#else
                        readerThread.Join();
#endif
                        // Save state.
                        Save();
                    }
                }
            }
        }
        #endregion Shutdown

        #region AppLoads
        /// <summary>
        /// Creates the application load report.
        /// </summary>
        private static void CreateAppLoadReport() {
            if (GetOptOutStatus()) {
                return;
            }
            AppLoad appLoad=new AppLoad();
            AddMessageToQueue(appLoad);
        }
        #endregion AppLoads

        #region Breadcrumbs
        /// <summary>
        /// Leave breadcrumb.
        /// </summary>
        /// <param name="breadcrumb">   The breadcrumb. </param>
        public static void LeaveBreadcrumb(string breadcrumb) {
            if (GetOptOutStatus()) {
                return;
            } else if (!initialized) {
                Debug.WriteLine(errorNotInitialized);
                return;
            };
            PrivateBreadcrumbs.LeaveBreadcrumb(breadcrumb);
        }
        #endregion Breadcrumbs

        #region Exceptions and Crashes
        internal static void LogInternalException(Exception e) {
            Debug.WriteLine("UNEXPECTED ERROR!!! "+e.Message);
            Debug.WriteLine(e.StackTrace);
            Debug.WriteLine("");
        }

        private static string StackTrace(Exception e) {
            // Allowing for the fact that the "name" and "reason" of the outermost
            // exception e are already shown in the Crittercism portal, we don't
            // need to repeat that bit of info.  However, for InnerException's, we
            // will include this information in the StackTrace .  The horizontal
            // lines (hyphens) separate InnerException's from each other and the
            // outermost Exception e .
            string answer=e.StackTrace;
            // Using seen for cycle detection to break cycling.
            List<Exception> seen=new List<Exception>();
            seen.Add(e);
            if (answer!=null) {
                // There has to be some way of telling where InnerException ie stacktrace
                // ends and main Exception e stacktrace begins.  This is it.
                answer=((e.GetType().FullName+" : "+e.Message+"\r\n")
                    +answer);
                Exception ie=e.InnerException;
                while ((ie!=null)&&(seen.IndexOf(ie)<0)) {
                    seen.Add(ie);
                    answer=((ie.GetType().FullName+" : "+ie.Message+"\r\n")
                        +(ie.StackTrace+"\r\n")
                        +answer);
                    ie=ie.InnerException;
                }
            } else {
                answer="";
            }
            return answer;
        }

        /// <summary>
        /// Creates handled exception report.
        /// </summary>
        public static void LogHandledException(Exception e) {
            if (GetOptOutStatus()) {
                return;
            } else if (!initialized) {
                Debug.WriteLine(errorNotInitialized);
                return;
            };
            Dictionary<string,string> metadata=CurrentMetadata();
            Breadcrumbs breadcrumbs=CurrentBreadcrumbs();
            string stacktrace=StackTrace(e);
            ExceptionObject exception=new ExceptionObject(e.GetType().FullName,e.Message,stacktrace);
            HandledException he=new HandledException(AppID,metadata,breadcrumbs,exception);
            AddMessageToQueue(he);
        }
        
        /// <summary>
        /// Creates a crash report.
        /// </summary>
        /// <param name="currentException"> The current exception. </param>
        internal static void LogUnhandledException(Exception e) {
            Dictionary<string,string> metadata=CurrentMetadata();
            Breadcrumbs breadcrumbs=PrivateBreadcrumbs.Copy();
            string stacktrace=StackTrace(e);
            ExceptionObject exception=new ExceptionObject(e.GetType().FullName,e.Message,stacktrace);
            Crash crash=new Crash(AppID,metadata,breadcrumbs,exception);
            // Add crash to message queue and save state .
            AddMessageToQueue(crash);
            Save();
            // App is probably going to crash now, because we choose not
            // to handle the unhandled exception ourselves and typically
            // most apps will choose to log the exception (e.g. with Crittercism)
            // but let the crash go ahead.
        }
        #endregion Exceptions and Crashes

        #region Metadata
        /// <summary>
        /// Sets "username" metadata value.
        /// </summary>
        /// <param name="username"> The username. </param>
        public static void SetUsername(string username) {
            // SetValue will check GetOptOutStatus() and initialized .
            SetValue("username",username);
        }

        /// <summary>
        /// Gets "username" metadata value.
        /// </summary>
        public static string Username() {
            // ValueFor will check GetOptOutStatus() and initialized .
            return ValueFor("username");
        }

        /// <summary>
        /// Sets a user metadata value.
        /// </summary>
        /// <param name="key">      The key. </param>
        /// <param name="value">    The value. </param>
        public static void SetValue(string key,string value) {
            if (GetOptOutStatus()) {
                return;
            } else if (!initialized) {
                Debug.WriteLine(errorNotInitialized);
                return;
            }
            try {
                lock (lockObject) {
                    if (!Metadata.ContainsKey(key)||!Metadata[key].Equals(value)) {
                        Metadata[key]=value;
                        UserMetadata metadata=new UserMetadata(
                            AppID,new Dictionary<string,string>(Metadata));
                        AddMessageToQueue(metadata);
                    }
                }
            } catch (Exception e) {
                Crittercism.LogInternalException(e);
                // explicit nop
            }
        }

        /// <summary>
        /// Returns a user metadata value.
        /// </summary>
        /// <param name="key">      The key. </param>
        public static string ValueFor(string key) {
            if (GetOptOutStatus()) {
                return null;
            } else if (!initialized) {
                Debug.WriteLine(errorNotInitialized);
                return null;
            };
            string answer=null;
            lock (lockObject) {
                if (Metadata.ContainsKey(key)) {
                    answer=Metadata[key];
                }
            }
            return answer;
        }
        #endregion Metadata

        #region MessageQueue
        /// <summary>
        /// Loads the messages from disk into the queue.
        /// </summary>
        private static void LoadQueue() {
            List<MessageReport> messages=MessageReport.LoadMessages();
            foreach (MessageReport message in messages) {
                // I'm wondering if we needed to restrict to 50 message of something similar?
                MessageQueue.Enqueue(message);
            }
        }

        private const int MaxMessageQueueCount=100;

        /// <summary>
        /// Adds message to queue
        /// </summary>
        private static void AddMessageToQueue(MessageReport message) {
            while (MessageQueue.Count>=MaxMessageQueueCount) {
                // Sacrifice an oldMessage
                MessageReport oldMessage=MessageQueue.Dequeue();
                oldMessage.Delete();
            }
            MessageQueue.Enqueue(message);
            readerEvent.Set();
        }
        #endregion // MessageQueue

        #region Event Handlers

#if NETFX_CORE
#pragma warning disable 1998
        private static async void Application_UnhandledException(object sender,UnhandledExceptionEventArgs args) {
            if (GetOptOutStatus()) {
                return;
            }
            try {
                LogUnhandledException(args.Exception);
            } catch (Exception e) {
                Crittercism.LogInternalException(e);
                // explicit nop
            }
        }

        static void NetworkInformation_NetworkStatusChanged(object sender) {
            if (GetOptOutStatus()) {
                return;
            }
            Debug.WriteLine("NetworkStatusChanged");
            ConnectionProfile profile=NetworkInformation.GetInternetConnectionProfile();
            bool isConnected=(profile!=null
                &&(profile.GetNetworkConnectivityLevel()==NetworkConnectivityLevel.InternetAccess));
            if (isConnected) {
                if (MessageQueue!=null&&MessageQueue.Count>0) {
                    readerEvent.Set();
                }
            }
        }
#elif WINDOWS_PHONE
        /// <summary>
        /// Event handler. Called by Current for unhandled exception events.
        /// </summary>
        /// <param name="sender">   Source of the event. </param>
        /// <param name="e">        Application unhandled exception event information. </param>
        static void SilverlightApplication_UnhandledException(object sender,ApplicationUnhandledExceptionEventArgs args) {
            if (GetOptOutStatus()) {
                return;
            }
            try {
                LogUnhandledException((Exception)args.ExceptionObject);
            } catch (Exception e) {
                Crittercism.LogInternalException(e);
                // explicit nop
            }
        }

        static void PhoneApplicationService_Activated(object sender, ActivatedEventArgs e) {
            if (GetOptOutStatus()) {
                return;
            }
            BackgroundWorker backgroundWorker = new BackgroundWorker();
            backgroundWorker.DoWork += new DoWorkEventHandler(BackgroundWorker_DoWork);
            backgroundWorker.RunWorkerAsync();
        }

        static void BackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            StartApplication((string)PhoneApplicationService.Current.State["Crittercism.AppID"]);
        }

        static void PhoneApplicationService_Deactivated(object sender, DeactivatedEventArgs e)
        {
            if (GetOptOutStatus()) {
                return;
            }
            PhoneApplicationService.Current.State["Crittercism.AppID"] = AppID;
        }

        static void DeviceNetworkInformation_NetworkAvailabilityChanged(object sender,NetworkNotificationEventArgs e) {
            if (GetOptOutStatus()) {
                return;
            }
            // This flag is for unit test
            if (_autoRunQueueReader) {
                switch (e.NotificationType) {
                    case NetworkNotificationType.InterfaceConnected:
                        if (NetworkInterface.GetIsNetworkAvailable()) {
                            if (MessageQueue!=null&&MessageQueue.Count>0) {
                                readerEvent.Set();
                            }
                        }
                        break;
                }
            }
        }
#else
        static void AppDomain_UnhandledException(object sender,UnhandledExceptionEventArgs args) {
            if (GetOptOutStatus()) {
                return;
            }
            try {
                LogUnhandledException((Exception)args.ExceptionObject);
            } catch (Exception e) {
                Crittercism.LogInternalException(e);
                // explicit nop
            }
        }

        private static void WindowsFormsApplication_ThreadException(object sender,ThreadExceptionEventArgs t) {
            ////////////////////////////////////////////////////////////////
            // Crittercism unhandled exception handler for Windows Forms apps.
            // Crittercism users must add
            //     Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            // to their Program.cs Main() .
            // MSDN: "Application.SetUnhandledExceptionMode Method (UnhandledExceptionMode)
            // Call SetUnhandledExceptionMode before you instantiate the main form
            // of your application using the Run method.
            // To catch exceptions that occur in threads not created and owned by
            // Windows Forms, use the UnhandledException event handler."
            // https://msdn.microsoft.com/en-us/library/ms157905(v=vs.110).aspx
            ////////////////////////////////////////////////////////////////
            LogUnhandledException(t.Exception);
        }
#endif

        #endregion // Event Handlers
    }
}
