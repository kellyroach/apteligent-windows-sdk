using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
#if WINDOWS_PHONE
using Windows.Devices.Sensors;
using Windows.Graphics.Display;
using Microsoft.Phone.Net.NetworkInformation;
#endif

namespace CrittercismSDK {
    [DataContract]
    internal class HandledException : MessageReport {
        /// <summary>
        /// Gets or sets the identifier of the application.
        /// </summary>
        /// <value> The identifier of the application. </value>
        [DataMember]
        public string app_id { get; internal set; }

        /// <summary>
        /// Gets or sets the application state.
        /// </summary>
        [DataMember]
        public Dictionary<string,object> app_state { get; internal set; }

        /// <summary>
        /// Gets or sets the error.
        /// </summary>
        /// <value> The error. </value>
        [DataMember]
        public ExceptionObject error { get; internal set; }

        /// <summary>
        /// Gets or sets the platform
        /// </summary>
        [DataMember]
        public Platform platform { get; internal set; }

        [DataMember]
        public UserBreadcrumbs breadcrumbs { get; internal set; }

        [DataMember]
        public List<Endpoint> endpoints { get; internal set; }

        [DataMember]
        public List<Breadcrumb> systemBreadcrumbs { get; internal set; }

        [DataMember]
        public Dictionary<string,string> metadata { get; internal set; }

        /// <summary>
        /// Default constructor.
        /// </summary>
        public HandledException() {
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="appId">     Identifier for the application. </param>
        /// <param name="exception"> The exception. </param>
        public HandledException(
            string appId,
            Dictionary<string,string> metadata,
            UserBreadcrumbs breadcrumbs,
            List<Endpoint> endpoints,
            List<Breadcrumb> systemBreadcrumbs,
            ExceptionObject exception) {
            app_id = appId;
            app_state = ComputeLegacyAppState();
            error = exception;
            this.metadata = metadata;
            this.breadcrumbs = breadcrumbs;
            this.endpoints = endpoints;
            this.systemBreadcrumbs = systemBreadcrumbs;
            platform = new Platform();
        }
    }
}
