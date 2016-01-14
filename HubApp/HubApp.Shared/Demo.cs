﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using CrittercismSDK;
using HubApp.Data;

namespace HubApp {
    class Demo {
        private static Random random = new Random();
        internal static void ItemClick(Frame frame,SampleDataItem item) {
            // Navigate to the appropriate destination page, configuring the new page
            // by passing required information as a navigation parameter
            string itemId = item.UniqueId;
            Debug.WriteLine("UniqueId == " + itemId);
            Crittercism.LeaveBreadcrumb("UniqueId == " + itemId);
            if (itemId.Equals("Set Username")) {
                Random random = new Random();
                string[] names = { "Blue Jay","Chinchilla","Chipmunk","Gerbil","Hamster","Parrot","Robin","Squirrel","Turtle" };
                string name = names[random.Next(0,names.Length)];
                Crittercism.SetUsername("Critter " + name);
            } else if (itemId.Equals("Leave Breadcrumb")) {
                Random random = new Random();
                string[] names = { "Breadcrumb","Strawberry","Seed","Grape","Lettuce" };
                string name = names[random.Next(0,names.Length)];
                Crittercism.LeaveBreadcrumb(name);
            } else if (itemId.Equals("Network Request")) {
                LogNetworkRequest();
            } else if (itemId.Equals("Handled Exception")) {
                {
                    try {
                        ThrowException();
                    } catch (Exception ex) {
                        Crittercism.LogHandledException(ex);
                    }
                }
            } else if (itemId.Equals("Begin UserFlow")) {
                UserFlowClick(frame,item);
            } else if (itemId.Equals("Crash")) {
                ThrowException();
            } else {
                // We are on "End UserFlow" SectionPage .
                if (itemId.Equals("Succeed")) {
                    Crittercism.EndUserFlow(userFlowName);
                } else if (itemId.Equals("Fail")) {
                    Crittercism.FailUserFlow(userFlowName);
                } else if (itemId.Equals("Cancel")) {
                    Crittercism.CancelUserFlow(userFlowName);
                };
                userFlowItem.Title = beginUserFlowLabel;
                frame.GoBack();
            }
        }
        private static string[] urls = new string[] {
            "http://www.hearst.com",
            "http://www.urbanoutfitters.com",
            "http://www.pinterest.com",
            "http://www.docusign.com",
            "http://www.netflix.com",
            "http://www.paypal.com",
            "http://www.groupon.com",
            "http://www.ebay.com",
            "http://www.yahoo.com",
            "http://www.linkedin.com",
            "http://www.bloomberg.com",
            "http://www.hoteltonight.com",
            "http://www.npr.org",
            "http://www.samsclub.com",
            "http://www.postmates.com",
            "http://www.teslamotors.com",
            "http://www.bhphotovideo.com",
            "http://www.getkeepsafe.com",
            "http://www.boltcreative.com",
            "http://www.crittercism.com/customers/"
        };
        private static void LogNetworkRequest() {
            Random random = new Random();
            string[] methods = new string[] { "GET","POST","HEAD","PUT" };
            string method = methods[random.Next(0,methods.Length)];
            string url = urls[random.Next(0,urls.Length)];
            if (random.Next(0,2) == 1) {
                url = url + "?doYouLoveCrittercism=YES";
            }
            // latency in milliseconds
            long latency = (long)Math.Floor(4000.0 * random.NextDouble());
            long bytesRead = random.Next(0,10000);
            long bytesSent = random.Next(0,10000);
            long responseCode = 200;
            if (random.Next(0,5) == 0) {
                // Some common response other than 200 == OK .
                long[] responseCodes = new long[] { 301,308,400,401,402,403,404,405,408,500,502,503 };
                responseCode = responseCodes[random.Next(0,responseCodes.Length)];
            }
            Crittercism.LogNetworkRequest(
                method,
                url,
                latency,
                bytesRead,
                bytesSent,
                (HttpStatusCode)responseCode,
                WebExceptionStatus.Success);
        }
        internal const string beginUserFlowLabel = "Begin UserFlow";
        internal const string endUserFlowLabel = "End UserFlow";
        private static string[] userFlowNames = new string[] { "Buy Critter Feed","Sing Critter Song","Write Critter Poem" };
        private static string userFlowName;
        private static SampleDataItem userFlowItem = null;
        private static void UserFlowClick(Frame frame,SampleDataItem item) {
            // Conveniently remembering userFlowItem so we can change its Title
            // back to "Begin UserFlow" later on.
            userFlowItem = item;
            if (item.Title == beginUserFlowLabel) {
                // "Begin UserFlow"
                userFlowName = userFlowNames[random.Next(0,userFlowNames.Length)];
                Crittercism.BeginUserFlow(userFlowName);
                item.Title = endUserFlowLabel;
            } else {
                // "End UserFlow"
                // This works because "End UserFlow" == UniqueId of Groups[1]
                frame.Navigate(typeof(SectionPage),item.Title);
            }
        }

        internal static async void UserFlowTimeOutHandler(Page page,EventArgs e) {
            // UserFlow timed out.
            await page.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,async () => {
                userFlowItem.Title = beginUserFlowLabel;
                if (page.Frame.Content == page) {
                    // This page is being shown.
                    await UserFlowTimeOutShowMessage(e);
                    if (page is SectionPage) {
                        SectionPage sectionPage = (SectionPage)page;
                        string title = sectionPage.Title();
                        Debug.WriteLine("PageTitle == " + title);
                        if (title == "End UserFlow") {
                            // If we find ourselves currently on the "End UserFlow" SectionPage .
                            Frame frame = page.Frame;
                            frame.GoBack();
                        }
                    }
                }
            });
        }

        private static async Task UserFlowTimeOutShowMessage(EventArgs e) {
            // Show MessageDialog routine for caller UserFlowTimeOutHandler
            string name = ((CRUserFlowEventArgs)e).Name;
            string message = String.Format("UserFlow '{0}'\r\nTimed Out",name);
            Debug.WriteLine(message);
            var messageDialog = new MessageDialog(message);
            messageDialog.Commands.Add(new UICommand("Close"));
            messageDialog.DefaultCommandIndex = 0;
            await messageDialog.ShowAsync();
        }

        private static void DeepError(int n) {
            if (n == 0) {
                throw new Exception("Deep Inner Exception");
            } else {
                DeepError(n - 1);
            }
        }

        private static void ThrowException() {
            DeepError(random.Next(0,4));
        }

        private static void OuterException() {
            try {
                DeepError(4);
            } catch (Exception ie) {
                throw new Exception("Outer Exception",ie);
            }
        }

    }
}
