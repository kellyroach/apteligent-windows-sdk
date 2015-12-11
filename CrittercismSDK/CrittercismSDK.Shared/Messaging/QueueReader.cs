using CrittercismSDK;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Net.NetworkInformation;
using System.Runtime.Serialization.Json;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Windows;
#if NETFX_CORE
using System.Threading.Tasks;
using Windows.UI.Xaml;
#elif WINDOWS_PHONE
#else
using System.Web;
#endif

namespace CrittercismSDK
{
    internal class QueueReader
    {
        /// <summary>
        /// Reads the queue.
        /// </summary>
        internal void ReadQueue() {
            Debug.WriteLine("ReadQueue: ENTER");
            try {
                while (Crittercism.initialized) {
                    ReadStep();
                    Debug.WriteLine("ReadQueue: SLEEP");
                    // wake up again 300000 milliseconds == 5 minute timeout from now
                    // even without prompting.  Useful if last SendMessage failed and
                    // it seems time to try again despite no new messages have poured
                    // into the MessageQueue which would have "Set" the readerEvent.
                    const int READQUEUE_MILLISECONDS_TIMEOUT=300000;
                    Crittercism.readerEvent.WaitOne(READQUEUE_MILLISECONDS_TIMEOUT);
                    Debug.WriteLine("ReadQueue: WAKE");
                };
            } catch (Exception ie) {
                Crittercism.LogInternalException(ie);
            }
            Debug.WriteLine("ReadQueue: EXIT");
        }

        private void ReadStep() {
            Debug.WriteLine("ReadStep: ENTER");
            try {
                int retry=0;
                while (Crittercism.initialized
                    &&(Crittercism.MessageQueue!=null)
                    &&(Crittercism.MessageQueue.Count>0)
                    &&(NetworkInterface.GetIsNetworkAvailable())
                    &&(retry<3)) {
                    if (SendMessage()) {
                        retry=0;
                    } else {
                        // TODO: Use System.Timers.Timer to generate an event
                        // 5 minutes from now, wait for it, then proceed.
                        retry++;
                        Debug.WriteLine("ReadStep: retry == "+retry);
                    }
                };
                if (Crittercism.initialized) {
                    // Opportune time to save Crittercism state.  Unable to make the MessageQueue
                    // shorter either because SendMessage failed or MessageQueue has gone empty.
                    // The readerThread will be going into a do nothing wait state after this.
                    // (If Crittercism.initialized==false, we are shut down or shutting down, and
                    // we must not call Crittercism.Save since this can lead to DEADLOCK.
                    // Crittercism.Shutdown may have lock on Crittercism.lockObject, and is waiting
                    // for our readerThread to exit.  Crittercism.Save would try to acquire
                    // Crittercism.lockObject, but can't.)
                    Crittercism.Save();
                };
            } catch (Exception ie) {
                Crittercism.LogInternalException(ie);
            }
            Debug.WriteLine("ReadStep: EXIT");
        }

        /// <summary>
        /// Send message to the endpoint.
        /// </summary>
        /// <returns>   true if it succeeds, false if it fails. </returns>
        private bool SendMessage() {
            //Debug.WriteLine("SendMessage: ENTER");
            bool sendCompleted = false;
            try {
                if ((Crittercism.MessageQueue != null) && (Crittercism.MessageQueue.Count > 0)) {
                    if (!Crittercism.enableSendMessage) {
                        // This case used by UnitTest .
                        sendCompleted = true;
                    } else if (NetworkInterface.GetIsNetworkAvailable()) {
                        MessageReport message = Crittercism.MessageQueue.Peek();
                        Crittercism.MessageQueue.Dequeue();
                        message.Delete();
                        try {
                            HttpWebRequest request = message.WebRequest();
                            if (request!=null) {
                                Debug.WriteLine("SendMessage: "+message.GetType().Name);
                                sendCompleted = SendRequest(request,message.PostBody());
                            }
                        } catch (Exception ie) {
                            Crittercism.LogInternalException(ie);
                        }
                        if (!sendCompleted) {
                            Crittercism.MessageQueue.Enqueue(message);
                        }
                    }
                };
            } catch (Exception ie) {
                Crittercism.LogInternalException(ie);
            }
            //Debug.WriteLine("SendMessage: EXIT ---> "+sendCompleted);
            return sendCompleted;
        }

#if WINDOWS_PHONE_APP
        private bool SendRequest(HttpWebRequest request,string postBody) {
            //Debug.WriteLine("SendRequest: request.RequestUri == "+request.RequestUri);
            bool sendCompleted =false;
            Debug.WriteLine("SendRequest: ENTER");
            try {
                Task<Stream> writerTask=request.GetRequestStreamAsync();
                using (Stream writer=writerTask.Result) {
                    // NOTE: SendRequest caller's request.ContentType=="application/json; charset=utf-8"
                    // or request.ContentType=="application/x-www-form-urlencoded"
                    Debug.WriteLine("SendRequest: POST BODY:");
                    Debug.WriteLine(postBody);
                    byte[] postBytes=Encoding.UTF8.GetBytes(postBody);
                    writer.Write(postBytes,0,postBytes.Length);
                    writer.Flush();
                }
                Task<WebResponse> responseTask=request.GetResponseAsync();
                using (HttpWebResponse response=(HttpWebResponse)responseTask.Result) {
                    try {
                        Debug.WriteLine("SendRequest: response.StatusCode == " + (int)response.StatusCode);
                        if ((((long)response.StatusCode)/100)==2) {
                            // 2xx Success
                            sendCompleted=true;
                        }
                    } catch (WebException webEx) {
                        Debug.WriteLine("SendRequest: webEx == " + webEx);
                        if (webEx.Response!=null) {
                            //Debug.WriteLine("SendRequest: response.StatusCode == "+(int)response.StatusCode);
                            if (response.StatusCode==HttpStatusCode.BadRequest) {
                                try {
                                    using (StreamReader errorReader=(new StreamReader(webEx.Response.GetResponseStream()))) {
                                        string errorMessage=errorReader.ReadToEnd();
                                        Debug.WriteLine("SendRequest: " + errorMessage);
                                    }
                                } catch {
                                }
                            }
                        }
                    } catch (Exception ex) {
                        Debug.WriteLine("SendRequest: ex == " + ex.Message);
                    }
                }
            } catch (Exception ie) {
                Crittercism.LogInternalException(ie);
            }
            Debug.WriteLine("SendRequest: EXIT ---> "+sendCompleted);
            return sendCompleted;
        }
#else
        private bool SendRequest(HttpWebRequest request,string postBody) {
            //Debug.WriteLine("SendRequest: request.RequestUri == "+request.RequestUri);
            bool sendCompleted=false;
            Debug.WriteLine("SendRequest: ENTER");
            try {
                ManualResetEvent resetEvent=new ManualResetEvent(false);
                request.BeginGetRequestStream(
                    (result) => {
                        //Debug.WriteLine("SendRequest: BeginGetRequestStream");
                        try {
                            using (Stream requestStream=request.EndGetRequestStream(result)) {
                                using (StreamWriter writer=new StreamWriter(requestStream)) {
                                    writer.Write(postBody);
                                    Debug.WriteLine("SendRequest: POST BODY:");
                                    Debug.WriteLine(postBody);
                                    writer.Flush();
#if NETFX_CORE
#else
                                    writer.Close();
#endif
                                }
                            }
                            request.BeginGetResponse(
                                (asyncResponse) => {
                                    //Debug.WriteLine("SendRequest: BeginGetResponse");
                                    try {
                                        using (HttpWebResponse response=(HttpWebResponse)request.EndGetResponse(asyncResponse)) {
                                            Debug.WriteLine("SendRequest: response.StatusCode == "+(int)response.StatusCode);
                                            if ((((long)response.StatusCode)/100)==2) {
                                                // 2xx Success
                                                sendCompleted=true;
                                            }
                                        }
                                    } catch (WebException webEx) {
                                        Debug.WriteLine("SendRequest: webEx == "+webEx);
                                        if (webEx.Response!=null) {
                                            using (HttpWebResponse response=(HttpWebResponse)webEx.Response) {
                                                //Debug.WriteLine("SendRequest: response.StatusCode == "+(int)response.StatusCode);
                                                if (response.StatusCode==HttpStatusCode.BadRequest) {
                                                    try {
                                                        using (StreamReader errorReader=(new StreamReader(webEx.Response.GetResponseStream()))) {
                                                            string errorMessage=errorReader.ReadToEnd();
                                                            Debug.WriteLine(errorMessage);
                                                        }
                                                    } catch {
                                                    }
                                                }
                                            }
                                        }
                                    } catch {
                                    }
                                    resetEvent.Set();
                                },null);
                        } catch {
                            resetEvent.Set();
                        }
                    },null);
                {
#if DEBUG
                    Stopwatch stopWatch=new Stopwatch();
                    stopWatch.Start();
#endif
                    resetEvent.WaitOne();
#if DEBUG
                    stopWatch.Stop();
                    Debug.WriteLine("SendRequest: TOTAL SECONDS == "+stopWatch.Elapsed.TotalSeconds);
#endif
                }
            } catch (Exception ie) {
                Crittercism.LogInternalException(ie);
            }
            Debug.WriteLine("SendRequest: EXIT ---> "+sendCompleted);
            return sendCompleted;
        }
#endif // WINDOWS_PHONE_APP

        internal static string ComputeFormPostBody(MetadataReport metadataReport) {
            string postBody="";
            postBody+="did="+metadataReport.platform.device_id+"&";
            postBody+="app_id="+metadataReport.app_id+"&";
            string metadataJson=JsonConvert.SerializeObject(metadataReport.metadata);
#if NETFX_CORE
            postBody+="metadata="+WebUtility.UrlEncode(metadataJson)+"&";
            postBody+="device_name="+WebUtility.UrlEncode(metadataReport.platform.device_model);
#else
            // Only .NETFramework 4.5 has WebUtility.UrlEncode, earlier version
            // .NETFramework 4.0 has HttpUtility.UrlEncode
            postBody+="metadata="+HttpUtility.UrlEncode(metadataJson)+"&";
            postBody+="device_name="+HttpUtility.UrlEncode(metadataReport.platform.device_model);
#endif
            return postBody;
        }

    }
}