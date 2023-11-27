using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.SIP.App;
using System.Collections.Concurrent;
using SIPSorcery.Net;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using SIPSorcery.Sys;
using System.Threading;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Windows;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog.Configuration;
using Serilog.Sinks;

namespace SIPClient1
{
    public partial class MainWindow : Window
    {
        private static SIPTransport _sipTransport;
        private SIPRegistrationUserAgent _regUserAgent;
        private static string DEFAULT_CALL_DESTINATION = "sip:aaron@127.0.0.1:7060;transport=tcp";
        private static string DEFAULT_TRANSFER_DESTINATION = "sip:*61@192.168.0.48";
        private static int SIP_LISTEN_PORT = 5060;
        private static int SIPS_LISTEN_PORT = 5061;
        private static ConcurrentDictionary<string, SIPUserAgent> _calls = new ConcurrentDictionary<string, SIPUserAgent>();
        private static ConcurrentDictionary<string, SIPRegistrationUserAgent> _registrations = new ConcurrentDictionary<string, SIPRegistrationUserAgent>();
        //public static MainWindow Instance { get; private set; }
        private static readonly string DEFAULT_DESTINATION_SIP_URI = "sip:helloworld@127.0.0.1:5060;transport=tcp";
        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        private SIPUserAgent _ua; // Class-level variable to manage the call.
        public MainWindow()
        {
            InitializeComponent();
            InitializeSIPComponents();
           
            //Instance = this;
        }
        private void InitializeSIPComponents()
        {
            _sipTransport = new SIPTransport();
            _ua = new SIPUserAgent(_sipTransport, null);
            
            UserNameTextBox.Text = "Magdy";
            PasswordBox.Password = "Soft_123";
            SipServerIpTextBox.Text = "localhost";
            SipServerPortTextBox.Text = "5060";
        }
        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            string username = UserNameTextBox.Text;
            string password = PasswordBox.Password;
            string server = SipServerIpTextBox.Text;
            string domain = server;
            int port = int.Parse(SipServerPortTextBox.Text);
            int expiry = 400;
            var regUserAgent = new SIPRegistrationUserAgent(_sipTransport, username, password, domain, expiry);
            regUserAgent.RegistrationFailed += (uri, err, res) => AppendToLog($"{uri}: Registration failed - {err}");
            regUserAgent.RegistrationTemporaryFailure += (uri, msg, res) => AppendToLog($"{uri}: Temporary registration failure - {msg}");
            regUserAgent.RegistrationRemoved += (uri, msg) => AppendToLog($"{uri}: Registration removed.");
            regUserAgent.RegistrationSuccessful += (uri, msg) => AppendToLog($"{uri}: Registration successful.");
            // Start the registration process.
            regUserAgent.Start();
        }
        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            // End call using _sipUserAgent...
        }
        private async void TransferCallButton_Click(object sender, RoutedEventArgs e)
        {
            //var transferUri = SIPURI.ParseSIPURI("sip:" + TransferDestinationTextBox.Text);
            // Transfer call using _sipUserAgent...
        }
        private void StartCall()
        {
            Console.WriteLine("SIPSorcery client user agent example.");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            ManualResetEvent exitMre = new ManualResetEvent(false);
            bool preferIPv6 = false;
            bool isCallHungup = false;
            bool hasCallFailed = false;

            Log = AddConsoleLogger(LogEventLevel.Verbose);

            SIPURI callUri = SIPURI.ParseSIPURI(DEFAULT_DESTINATION_SIP_URI);
            //if (args?.Length > 0)
            //{
            //    if (!SIPURI.TryParse(args[0], out callUri))
            //    {
            //        AppendToLog($"Command line argument could not be parsed as a SIP URI {args[0]}");
            //    }
            //}
            //if (args?.Length > 1 && args[1] == "ipv6")
            //{
            //    preferIPv6 = true;
            //}

            if (preferIPv6)
            {
                AppendToLog($"Call destination {callUri}, preferencing IPv6.");
            }
            else
            {
                AppendToLog($"Call destination {callUri}.");
            }

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();
            sipTransport.PreferIPv6NameResolution = preferIPv6;
            sipTransport.EnableTraceLogs();

            var audioSession = new WindowsAudioEndPoint(new AudioEncoder());
            audioSession.RestrictFormats(x => x.Codec == AudioCodecsEnum.PCMA || x.Codec == AudioCodecsEnum.PCMU);
            //audioSession.RestrictFormats(x => x.Codec == AudioCodecsEnum.G722);
            var rtpSession = new VoIPMediaSession(audioSession.ToMediaEndPoints());

            var offerSDP = rtpSession.CreateOffer(preferIPv6 ? IPAddress.IPv6Any : IPAddress.Any);

            // Create a client user agent to place a call to a remote SIP server along with event handlers for the different stages of the call.
            var uac = new SIPClientUserAgent(sipTransport);
            uac.CallTrying += (uac, resp) => AppendToLog($"{uac.CallDescriptor.To} Trying: {resp.StatusCode} {resp.ReasonPhrase}.");
            uac.CallRinging += async (uac, resp) =>
            {
                AppendToLog($"{uac.CallDescriptor.To} Ringing: {resp.StatusCode} {resp.ReasonPhrase}.");
                if (resp.Status == SIPResponseStatusCodesEnum.SessionProgress)
                {
                    if (resp.Body != null)
                    {
                        var result = rtpSession.SetRemoteDescription(SdpType.answer, SDP.ParseSDPDescription(resp.Body));
                        if (result == SetDescriptionResultEnum.OK)
                        {
                            await rtpSession.Start();
                            AppendToLog($"Remote SDP set from in progress response. RTP session started.");
                        }
                    }
                }
            };
            uac.CallFailed += (uac, err, resp) =>
            {
                AppendToLog($"Call attempt to {uac.CallDescriptor.To} Failed: {err}");
                hasCallFailed = true;
            };
            uac.CallAnswered += async (iuac, resp) =>
            {
                if (resp.Status == SIPResponseStatusCodesEnum.Ok)
                {
                    AppendToLog($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");

                    if (resp.Body != null)
                    {
                        var result = rtpSession.SetRemoteDescription(SdpType.answer, SDP.ParseSDPDescription(resp.Body));
                        if (result == SetDescriptionResultEnum.OK)
                        {
                            await rtpSession.Start();
                        }
                        else
                        {
                            AppendToLog($"Failed to set remote description {result}.");
                            uac.Hangup();
                        }
                    }
                    else if (!rtpSession.IsStarted)
                    {
                        AppendToLog($"Failed to set get remote description in session progress or final response.");
                        uac.Hangup();
                    }
                }
                else
                {
                    AppendToLog($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");
                }
            };

            // The only incoming request that needs to be explicitly handled for this example is if the remote end hangs up the call.
            sipTransport.SIPTransportRequestReceived += async (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
            {
                if (sipRequest.Method == SIPMethodsEnum.BYE)
                {
                    SIPResponse okResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    await sipTransport.SendResponseAsync(okResponse);

                    if (uac.IsUACAnswered)
                    {
                        AppendToLog("Call was hungup by remote server.");
                        isCallHungup = true;
                        exitMre.Set();
                    }
                }
            };

            // Start the thread that places the call.
            SIPCallDescriptor callDescriptor = new SIPCallDescriptor(
                SIPConstants.SIP_DEFAULT_USERNAME,
                null,
                callUri.ToString(),
                SIPConstants.SIP_DEFAULT_FROMURI,
                callUri.CanonicalAddress,
                null, null, null,
                SIPCallDirection.Out,
                SDP.SDP_MIME_CONTENTTYPE,
                offerSDP.ToString(),
                null);

            uac.Call(callDescriptor, null);

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitMre.Set();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();

            AppendToLog("Exiting...");

            rtpSession.Close(null);

            if (!isCallHungup && uac != null)
            {
                if (uac.IsUACAnswered)
                {
                    AppendToLog($"Hanging up call to {uac.CallDescriptor.To}.");
                    uac.Hangup();
                }
                else if (!hasCallFailed)
                {
                    AppendToLog($"Cancelling call to {uac.CallDescriptor.To}.");
                    uac.Cancel();
                }

                // Give the BYE or CANCEL request time to be transmitted.
                AppendToLog("Waiting 1s for call to clean up...");
                Task.Delay(1000).Wait();
            }

            if (sipTransport != null)
            {
                AppendToLog("Shutting down SIP transport...");
                sipTransport.Shutdown();
            }
        }
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger(
          LogEventLevel logLevel = LogEventLevel.Debug)
        {
            var serilogLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(logLevel)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(serilogLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<MainWindow>();
        }
        private void StartCallButton_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(StartCall);
        }
        private void EndCall()
        {
            // Logic to end a call.
            if (_calls.TryRemove(_ua.Dialogue.CallId, out var ua))
            {
                ua.Hangup();
                AppendToLog("Call ended successfully.");
            }
            else
            {
                AppendToLog("Failed to end the call.");
            }
        }
        private void EndCallButton_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(EndCall);
        }
        private void AppendToLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                EventsInfoTextBox.AppendText(message + Environment.NewLine);
            });
        }
    }
}