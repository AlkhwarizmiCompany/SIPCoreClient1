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
        private SIPUserAgent _ua; // Class-level variable to manage the call.
        public MainWindow()
        {
            InitializeComponent();
            InitializeSIPComponents();
            _ua = new SIPUserAgent(_sipTransport, null);
            //Instance = this;
        }
        private void InitializeSIPComponents()
        {
            _sipTransport = new SIPTransport();
        }
        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            string username = UserNameTextBox.Text;
            string password = PasswordBox.Password;
            string server = SipServerIpTextBox.Text;
            string domain = server;
            int port = int.Parse(SipServerPortTextBox.Text);
            int expiry = 120;
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