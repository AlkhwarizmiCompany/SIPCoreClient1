using System.Net;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Forms.Design;
using NAudio.Wave;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Windows;

namespace SIPClient1
{
    public partial class MainWindow : Window
    {
        private static readonly string DefaultDestinationSipUri = "sip:127.0.0.1:5060";
        private static readonly WaveFormat WaveFormat = new WaveFormat(8000, 16, 1);
        private static readonly string fileName = "MainWindow.xaml.cs";

        private SIPTransport _sipTransport;
        private SIPClientUserAgent _userAgent;
        private BufferedWaveProvider _bufferedWaveProvider;
        private VoIPMediaSession _rtpSession;
        private Logger _logger;
        LogDocument logDocument = new LogDocument();
        LogEntry logEntry = new LogEntry();
        public MainWindow()
        {
            InitializeComponent();
            InitializeSIPComponents();
            InitializeAudioComponents();

            _logger = new Logger("mongodb://localhost:27017", "SIPAgent", "Logs");
        }

        private void InitializeSIPComponents()
        {
            _sipTransport = new SIPTransport();
            SetDefaultSIPUserDetails();
        }

        private void SetDefaultSIPUserDetails()
        {
            UserNameTextBox.Text = "Magdy";
            PasswordBox.Password = "Soft_123";
            SipServerIpTextBox.Text = "localhost";
            SipServerPortTextBox.Text = "5060";
        }

        private void InitializeAudioComponents()
        {
            _bufferedWaveProvider = new BufferedWaveProvider(WaveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(20)
            };
        }
  
        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            RegisterToSIPServer();
        }

        private void RegisterToSIPServer()
        {
            string username = UserNameTextBox.Text;
            string password = PasswordBox.Password;
            string server = SipServerIpTextBox.Text;
            string domain = server;

            int expiry = 400;
            var regUserAgent = new SIPRegistrationUserAgent(_sipTransport, username, password, domain, expiry);
            SubscribeToRegistrationEvents(regUserAgent);
            regUserAgent.Start();
        }

        private void SubscribeToRegistrationEvents(SIPRegistrationUserAgent regUserAgent)
        {
            regUserAgent.RegistrationFailed += (uri, err, res) =>
                AppendToLog($"{uri}: Registration failed - {err}",  "ERROR");

            regUserAgent.RegistrationTemporaryFailure += (uri, msg, res) =>
                AppendToLog($"{uri}: Temporary registration failure - {msg}", "WARNING");

            regUserAgent.RegistrationRemoved += (uri, msg) =>
                AppendToLog($"{uri}: Registration removed.", "INFO");

            regUserAgent.RegistrationSuccessful += (uri, msg) =>
                AppendToLog($"{uri}: Registration successful.", "INFO");
        }

        private async void AppendToLog(string message, string logLevel = "INFO")
        {
            Dispatcher.Invoke(() => EventsInfoTextBox.AppendText(message + Environment.NewLine));

            logEntry = new LogEntry
            {
                Filename = fileName,
                LogLevel = logLevel,
                Message = message,
                Timestamp = DateTime.UtcNow
            };

            try
            {
                await _logger.LogAsync(logDocument.CallId, logEntry);
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => EventsInfoTextBox.AppendText($"Logging to database failed: {ex.Message}\n"));
            }
        }

        private void StartCallButton_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(StartCall);
        }

        private void StartCall()
        {
            ManualResetEvent exitMre = new ManualResetEvent(false);
            bool preferIPv6 = false;
            bool isCallHungup = false;
            bool hasCallFailed = false;

            SIPURI callUri = SIPURI.ParseSIPURI(DefaultDestinationSipUri);

            _sipTransport.PreferIPv6NameResolution = preferIPv6;
            _sipTransport.EnableTraceLogs();

            var audioSession = new WindowsAudioEndPoint(new AudioEncoder());
            audioSession.RestrictFormats(x => x.Codec == AudioCodecsEnum.PCMA);
            _rtpSession = new VoIPMediaSession(audioSession.ToMediaEndPoints());
            _rtpSession.OnRtpPacketReceived += OnRtpPacketReceived;

            var offerSDP = _rtpSession.CreateOffer(preferIPv6 ? IPAddress.IPv6Any : IPAddress.Any);
            _userAgent = new SIPClientUserAgent(_sipTransport);
            SetupUserAgentEventHandlers();

            var callDescriptor = new SIPCallDescriptor(
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

            _userAgent.Call(callDescriptor, null);
            exitMre.WaitOne();
            CleanupAfterCall(exitMre, isCallHungup, hasCallFailed);
        }
        private void SetupUserAgentEventHandlers()
        {
            //_userAgent.CallTrying += (uac, resp) => AppendToLog($"{uac.CallDescriptor.To} Trying: {resp.StatusCode} {resp.ReasonPhrase}.");
           
            _userAgent.CallRinging += async (uac, resp) =>
            {
                logDocument = new LogDocument
                {
                    CallId = uac.CallDescriptor.CallId,
                    From = uac.CallDescriptor.From, 
                    To = uac.CallDescriptor.To,
                    CallUri = uac.CallDescriptor.Uri,
                };

                // create the call 
                await _logger.CreateOrUpdateLogDocumentAsync(logDocument);

                AppendToLog($"{uac.CallDescriptor.To} Ringing: {resp.StatusCode} {resp.ReasonPhrase}.");

                if (resp.Status == SIPResponseStatusCodesEnum.SessionProgress && resp.Body != null)
                {
                    var result = _rtpSession.SetRemoteDescription(SdpType.answer, SDP.ParseSDPDescription(resp.Body));
                    if (result == SetDescriptionResultEnum.OK)
                    {
                        await _rtpSession.Start();
                        AppendToLog("Remote SDP set from in progress response. RTP session started.");
                    }
                }
            };
            _userAgent.CallFailed += (uac, err, resp) =>
            {
                AppendToLog($"Call attempt to {uac.CallDescriptor.To} Failed: {err}", "ERROR");
            };
            _userAgent.CallAnswered += (iuac, resp) =>
            {
                AppendToLog($"{iuac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");
                if (resp.Status == SIPResponseStatusCodesEnum.Ok && resp.Body != null)
                {
                    var result = _rtpSession.SetRemoteDescription(SdpType.answer, SDP.ParseSDPDescription(resp.Body));
                    if (result == SetDescriptionResultEnum.OK)
                    {
                        Task.Run(() => PlayAudio());
                        _rtpSession.Start();
                    }
                    else
                    {
                        AppendToLog($"Failed to set remote description {result}.", "ERROR");
                        _userAgent.Hangup();
                    }
                }
            };
        }
        private void CleanupAfterCall(ManualResetEvent exitMre, bool isCallHungup, bool hasCallFailed)
        {
            AppendToLog("Exiting...");

            _rtpSession.Close(null);

            if (!isCallHungup && _userAgent != null)
            {
                if (_userAgent.IsUACAnswered)
                {
                    AppendToLog($"Hanging up call to {_userAgent.CallDescriptor.To}.");
                    _userAgent.Hangup();
                }
                else if (!hasCallFailed)
                {
                    AppendToLog($"Cancelling call to {_userAgent.CallDescriptor.To}.");
                    _userAgent.Cancel();
                }

                AppendToLog("Waiting 1s for call to clean up...", "WARNING");
                Thread.Sleep(1000);
            }

            if (_sipTransport != null)
            {
                AppendToLog("Shutting down SIP transport...");
                _sipTransport.Shutdown();
            }

            exitMre.Set();
        }
        private void EndCallButton_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(EndCall);
        }
        private void PlayAudio()
        {
            using (WaveOutEvent waveOut = new WaveOutEvent())
            {
                waveOut.Init(_bufferedWaveProvider);
                waveOut.Play();

                while (waveOut.PlaybackState == PlaybackState.Playing || _bufferedWaveProvider.BufferedDuration > TimeSpan.Zero)
                {
                    Thread.Sleep(100);
                }
            }
        }

        private void EndCall()
        {
            if (_userAgent != null)
            {
                _rtpSession.Close(null);
                _userAgent.Hangup();
                AppendToLog("Call ended successfully.");
            }
            else
            {
                AppendToLog("Failed to end the call.","ERROR");
            }
        }

        private void OnRtpPacketReceived(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                ProcessAudioPacket(rtpPacket);
            }
        }

        private void ProcessAudioPacket(RTPPacket rtpPacket)
        {
            var sample = rtpPacket.Payload;
            byte[] pcmSample = new byte[sample.Length * 2];
            int pcmIndex = 0;

            for (int index = 0; index < sample.Length; index++)
            {
                short pcm = DecodeSample(sample[index], rtpPacket.Header.PayloadType);
                pcmSample[pcmIndex++] = (byte)(pcm & 0xFF);
                pcmSample[pcmIndex++] = (byte)(pcm >> 8);
            }

            _bufferedWaveProvider.AddSamples(pcmSample, 0, pcmSample.Length);
        }


        private short DecodeSample(byte sample, int payloadType)
        {
            if (payloadType == (int)SDPWellKnownMediaFormatsEnum.PCMA)
            {
                return NAudio.Codecs.ALawDecoder.ALawToLinearSample(sample);
            }
            else
            {
                return NAudio.Codecs.MuLawDecoder.MuLawToLinearSample(sample);
            }
        }

       

    }
}
