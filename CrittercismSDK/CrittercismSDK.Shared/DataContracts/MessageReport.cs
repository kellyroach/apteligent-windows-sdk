// file:	CrittercismSDK\MessageReport.cs
// summary:	Implements the message report class
namespace CrittercismSDK.DataContracts {
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
#if WINDOWS_PHONE
    using Microsoft.Phone.Info;
    using Windows.Devices.Sensors;
    using Windows.Graphics.Display;
    using Microsoft.Phone.Net.NetworkInformation;
#endif

    /// <summary>
    /// Message report.
    /// </summary>
    public abstract class MessageReport {
        /// <summary>
        /// Gets or sets the file name.  E.g. Crash_guid
        /// </summary>
        /// <value> The name. </value>
        internal string Name { get; set; }

        /// <summary>
        /// Gets or sets the date of the file creation.
        /// </summary>
        /// <value> The date of the creation. </value>
        internal DateTimeOffset CreationTime { get; set; }

        internal static string DateTimeString(DateTime dt) {
            return dt.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ssK", CultureInfo.InvariantCulture);
        }

        protected Dictionary<string,object> ComputeAppState() {
            // Getting lots of stuff here. Some things like "DeviceId" require manifest-level authorization so skipping
            // those for now, see http://msdn.microsoft.com/en-us/library/ff769509%28v=vs.92%29.aspx#BKMK_Capabilities

            return new Dictionary<string, object> {
                { "app_version", Crittercism.AppVersion },
                // RemainingChargePercent returns an integer in [0,100]
#if WINDOWS_PHONE
                { "battery_level", Windows.Phone.Devices.Power.Battery.GetDefault().RemainingChargePercent / 100.0 },
                { "carrier", DeviceNetworkInformation.CellularMobileOperator },
                { "device_total_ram_bytes", DeviceExtendedProperties.GetValue("DeviceTotalMemory") },
                { "memory_usage", DeviceExtendedProperties.GetValue("ApplicationCurrentMemoryUsage") },
                { "memory_usage_peak", DeviceExtendedProperties.GetValue("ApplicationPeakMemoryUsage") },
                { "on_cellular_data", DeviceNetworkInformation.IsCellularDataEnabled },
                { "on_wifi", DeviceNetworkInformation.IsWiFiEnabled },
                { "orientation", DisplayProperties.NativeOrientation.ToString() },
#endif
                { "disk_space_free", StorageHelper.AvailableFreeSpace() },
                // skipping "name" for device name as it requires manifest approval
                { "locale", CultureInfo.CurrentCulture.Name},
                // all counters below in bytes
                { "reported_at", DateTimeString(DateTime.Now) }
            };
        }


        /// <summary>
        /// Saves the message to disk.
        /// </summary>
        /// <returns>   true if it succeeds, false if it fails. </returns>
        public bool Save() {
            // TODO(DA): On-disk serialization in JSON isn't type-preserving.
            // This isn't intended for transmission or portability, so should use something more
            // typesafe, even if it's C#-specific
            bool answer=false;
            try {
                Name=this.GetType().Name+"_"+Guid.NewGuid().ToString()+".js";
                string path=Path.Combine(folderName,Name);
                StorageHelper.Save(this,path);
                answer=true;
            } catch (Exception e) {
                Crittercism.LogInternalException(e);
            };
            return answer;
        }

        /// <summary>
        /// Deletes the message from disk.
        /// </summary>
        /// <returns>   true if it succeeds, false if it fails. </returns>
        internal bool Delete() {
            bool answer=false;
            try {
                if (StorageHelper.FolderExists(folderName)) {
                    string path=Path.Combine(folderName,this.Name);
                    if (StorageHelper.FileExists(path)) {
                        StorageHelper.DeleteFile(path);
                    }
                    answer=true;
                }
            } catch (Exception e) {
                Crittercism.LogInternalException(e);
            };
            return answer;
        }

        internal static string folderName="Messages";
        internal static List<MessageReport> LoadMessages() {
            List<MessageReport> messages=new List<MessageReport>();
            if (StorageHelper.FolderExists(folderName)) {
                string[] names=StorageHelper.GetFileNames(folderName);
                foreach (string name in names) {
                    MessageReport message=LoadMessage(name);
                    if (message!=null) {
                        messages.Add(message);
                    }
                }
                messages.Sort((m1,m2) => m1.CreationTime.CompareTo(m2.CreationTime));
            }
            return messages;
        }

        internal static MessageReport LoadMessage(string name) {
            // name is wrt folderName "Messages", e.g "Crash_<guid>"
            // path is Messages\name
            MessageReport message=null;
            try {
                string path=Path.Combine(folderName,name);
                string[] nameSplit=name.Split('_');
                switch (nameSplit[0]) {
                    case "AppLoad":
                        message=(AppLoad)StorageHelper.Load(path,typeof(AppLoad));
                        break;
                    case "HandledException":
                        message=(HandledException)StorageHelper.Load(path,typeof(HandledException));
                        break;
                    case "Crash":
                        message=(Crash)StorageHelper.Load(path,typeof(Crash));
                        break;
                    case "UserMetadata":
                        message=(UserMetadata)StorageHelper.Load(path,typeof(UserMetadata));
                        break;
                    default:
                        // Skip this file.
                        break;
                }
                if (message==null) {
                    // Our fault tolerant strategy here is to just return null
                    Debug.WriteLine("UNEXPECTED ERROR!!! Couldn't Load "+path);
                    StorageHelper.DeleteFile(path);
                } else {
                    message.Name=name;
                    message.CreationTime=StorageHelper.GetCreationTime(path);
                }
            } catch (Exception e) {
                Crittercism.LogInternalException(e);
            }
            return message;
        }

        static MessageReport() {
            if (!StorageHelper.FolderExists(folderName)) {
                StorageHelper.CreateFolder(folderName);
            }
        }
    }
}