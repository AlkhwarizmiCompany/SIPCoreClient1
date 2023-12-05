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
using NAudio.Wave;
using System.IO;

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
        private static readonly string DEFAULT_DESTINATION_SIP_URI = "sip:127.0.0.1:5060";
        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        private SIPUserAgent _ua; // Class-level variable to manage the call.
        private SIPClientUserAgent uac; // Class-level variable to manage the call.

        private BufferedWaveProvider _bufferedWaveProvider;

        private static readonly WaveFormat _waveFormat = new WaveFormat(8000, 16, 1);
        private static WaveFileWriter _waveFile;
        private static VoIPMediaSession rtpSession;
        public MainWindow()
        {
            InitializeComponent();
            InitializeSIPComponents();

            _bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(8000, 16, 1)); // Adjust the format as needed
            _bufferedWaveProvider.BufferDuration = TimeSpan.FromSeconds(20);

            _waveFile = new WaveFileWriter("G:\\src\\SIP\\SIPClient1\\SIPClient1\\output.mp3", _waveFormat);

        }
        private void InitializeSIPComponents()
        {
            //_sipTransport = new SIPTransport();
            //_ua = new SIPUserAgent(_sipTransport, null);
            //_ua.OnCallHungup += (dialog) => _waveFile?.Close();

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

        private void PlayByteArray()
        {
            using (WaveOutEvent waveOut = new WaveOutEvent())
            {
                waveOut.Init(_bufferedWaveProvider);
                waveOut.Play();

                while (waveOut.PlaybackState == PlaybackState.Playing || _bufferedWaveProvider.BufferedDuration > TimeSpan.Zero)
                {
                    System.Threading.Thread.Sleep(100);
                }
            }
        }




        private static void OnRtpPacketReceived1(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                var sample = rtpPacket.Payload;

                for (int index = 0; index < sample.Length; index++)
                {
                    if (rtpPacket.Header.PayloadType == (int)SDPWellKnownMediaFormatsEnum.PCMA)
                    {
                        short pcm = NAudio.Codecs.ALawDecoder.ALawToLinearSample(sample[index]);
                        byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                        _waveFile.Write(pcmSample, 0, 2);
                    }
                    else
                    {
                        short pcm = NAudio.Codecs.MuLawDecoder.MuLawToLinearSample(sample[index]);
                        byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                        _waveFile.Write(pcmSample, 0, 2);
                    }
                }
            }
        }
        private void OnRtpPacketReceived(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                var sample = rtpPacket.Payload;

                byte[] pcmSample = new byte[sample.Length * 2]; // PCM is 2 bytes per sample
                int pcmIndex = 0;

                for (int index = 0; index < sample.Length; index++)
                {
                    short pcm;
                    if (rtpPacket.Header.PayloadType == (int)SDPWellKnownMediaFormatsEnum.PCMA)
                    {
                        pcm = NAudio.Codecs.ALawDecoder.ALawToLinearSample(sample[index]);
                    }
                    else
                    {
                        pcm = NAudio.Codecs.MuLawDecoder.MuLawToLinearSample(sample[index]);
                    }

                    // Convert the short to bytes and store it in the pcmSample array
                    pcmSample[pcmIndex++] = (byte)(pcm & 0xFF);
                    pcmSample[pcmIndex++] = (byte)(pcm >> 8);
                }

                // Add the pcmSample to the BufferedWaveProvider
                _bufferedWaveProvider.AddSamples(pcmSample, 0, pcmSample.Length);
            }
        }



        private void StartCall()
        {
            // Plumbing code to facilitate a graceful exit.
            ManualResetEvent exitMre = new ManualResetEvent(false);
            bool preferIPv6 = false;
            bool isCallHungup = false;
            bool hasCallFailed = false;


            SIPURI callUri = SIPURI.ParseSIPURI(DEFAULT_DESTINATION_SIP_URI);

            AppendToLog($"Call destination {callUri}.");
            

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();
            sipTransport.PreferIPv6NameResolution = preferIPv6;
            sipTransport.EnableTraceLogs();

            var audioSession = new WindowsAudioEndPoint(new AudioEncoder());

            List<AudioCodecsEnum> codecs = new List<AudioCodecsEnum> { AudioCodecsEnum.PCMU, AudioCodecsEnum.PCMA, AudioCodecsEnum.G722, AudioCodecsEnum.L16 };
            //audioSession.RestrictFormats(formats => codecs.Contains(formats.Codec));

            //audioSession.RestrictFormats(x => x.Codec == AudioCodecsEnum.PCMA || x.Codec == AudioCodecsEnum.PCMU);
            //audioSession.RestrictFormats(x => x.Codec == AudioCodecsEnum.G722);
            audioSession.RestrictFormats(x => x.Codec == AudioCodecsEnum.PCMA);
            rtpSession = new VoIPMediaSession(audioSession.ToMediaEndPoints());

            rtpSession.OnRtpPacketReceived += OnRtpPacketReceived;
            var offerSDP = rtpSession.CreateOffer(preferIPv6 ? IPAddress.IPv6Any : IPAddress.Any);

            // Create a client user agent to place a call to a remote SIP server along with event handlers for the different stages of the call.
            uac = new SIPClientUserAgent(sipTransport);
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
                            Task.Run(() => PlayByteArray());
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
            var c = offerSDP.ToString();

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

        private void StartCallButton_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(StartCall);
        }
        private void EndCall()
        {
            // Logic to end a call.
            if (uac != null)
            {
                rtpSession.Close(null);

                uac.Hangup();
                _waveFile?.Close();
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