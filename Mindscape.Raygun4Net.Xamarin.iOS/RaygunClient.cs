﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using Mindscape.Raygun4Net.Messages;

using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.IO.IsolatedStorage;
using System.IO;
using System.Text;
using MonoTouch;
using System.Diagnostics;

#if __UNIFIED__
using UIKit;
using SystemConfiguration;
using Foundation;
using Security;
using ObjCRuntime;
#else
using MonoTouch.UIKit;
using MonoTouch.SystemConfiguration;
using MonoTouch.Foundation;
using MonoTouch.Security;
using MonoTouch.ObjCRuntime;
#endif

namespace Mindscape.Raygun4Net
{
  public class RaygunClient : RaygunClientBase
  {
    private readonly string _apiKey;
    private readonly List<Type> _wrapperExceptions = new List<Type>();
    private string _user;
    private RaygunIdentifierMessage _userInfo;

    /// <summary>
    /// Initializes a new instance of the <see cref="RaygunClient" /> class.
    /// </summary>
    /// <param name="apiKey">The API key.</param>
    public RaygunClient(string apiKey)
    {
      _apiKey = apiKey;

      _wrapperExceptions.Add(typeof(TargetInvocationException));
      _wrapperExceptions.Add(typeof(AggregateException));

      ThreadPool.QueueUserWorkItem(state => { SendStoredMessages(0); });
    }

    private bool ValidateApiKey()
    {
      if (string.IsNullOrEmpty(_apiKey))
      {
        System.Diagnostics.Debug.WriteLine("ApiKey has not been provided, exception will not be logged");
        return false;
      }
      return true;
    }

    /// <summary>
    /// Gets or sets the user identity string.
    /// </summary>
    public override string User
    {
      get { return _user; }
      set
      {
        _user = value;
        if (_reporter != null)
        {
          _reporter.Identify(_user);
        }
      }
    }

    /// <summary>
    /// Gets or sets information about the user including the identity string.
    /// </summary>
    public override RaygunIdentifierMessage UserInfo
    {
      get { return _userInfo; }
      set
      {
        _userInfo = value;
        if (_reporter != null)
        {
          if (_userInfo != null) {
            var info = new Mindscape.Raygun4Net.Xamarin.iOS.RaygunUserInfo ();
            info.Identifier = _userInfo.Identifier;
            info.IsAnonymous = _userInfo.IsAnonymous;
            info.Email = _userInfo.Email;
            info.FullName = _userInfo.FullName;
            info.FirstName = _userInfo.FirstName;
            _reporter.IdentifyWithUserInfo (info);
          } else {
            _reporter.IdentifyWithUserInfo (null);
          }
        }
      }
    }

    /// <summary>
    /// Gets or sets the maximum number of milliseconds allowed to attempt a synchronous send to Raygun.
    /// A value of 0 will use a timeout of 100 seconds.
    /// The default is 0.
    /// </summary>
    /// <value>The synchronous timeout in milliseconds.</value>
    public int SynchronousTimeout { get; set; }

    /// <summary>
    /// Adds a list of outer exceptions that will be stripped, leaving only the valuable inner exception.
    /// This can be used when a wrapper exception, e.g. TargetInvocationException or AggregateException,
    /// contains the actual exception as the InnerException. The message and stack trace of the inner exception will then
    /// be used by Raygun for grouping and display. The above two do not need to be added manually,
    /// but if you have other wrapper exceptions that you want stripped you can pass them in here.
    /// </summary>
    /// <param name="wrapperExceptions">Exception types that you want removed and replaced with their inner exception.</param>
    public void AddWrapperExceptions(params Type[] wrapperExceptions)
    {
      foreach (Type wrapper in wrapperExceptions)
      {
        if (!_wrapperExceptions.Contains(wrapper))
        {
          _wrapperExceptions.Add(wrapper);
        }
      }
    }

    /// <summary>
    /// Specifies types of wrapper exceptions that Raygun should send rather than stripping out and sending the inner exception.
    /// This can be used to remove the default wrapper exceptions (TargetInvocationException and AggregateException).
    /// </summary>
    /// <param name="wrapperExceptions">Exception types that should no longer be stripped away.</param>
    public void RemoveWrapperExceptions(params Type[] wrapperExceptions)
    {
      foreach (Type wrapper in wrapperExceptions)
      {
        _wrapperExceptions.Remove(wrapper);
      }
    }

    /// <summary>
    /// Transmits an exception to Raygun.io synchronously, using the version number of the originating assembly.
    /// </summary>
    /// <param name="exception">The exception to deliver.</param>
    public override void Send(Exception exception)
    {
      Send(exception, null, (IDictionary)null);
    }

    /// <summary>
    /// Transmits an exception to Raygun.io synchronously specifying a list of string tags associated
    /// with the message for identification. This uses the version number of the originating assembly.
    /// </summary>
    /// <param name="exception">The exception to deliver.</param>
    /// <param name="tags">A list of strings associated with the message.</param>
    public void Send(Exception exception, IList<string> tags)
    {
      Send(exception, tags, (IDictionary)null);
    }

    /// <summary>
    /// Transmits an exception to Raygun.io synchronously specifying a list of string tags associated
    /// with the message for identification, as well as sending a key-value collection of custom data.
    /// This uses the version number of the originating assembly.
    /// </summary>
    /// <param name="exception">The exception to deliver.</param>
    /// <param name="tags">A list of strings associated with the message.</param>
    /// <param name="userCustomData">A key-value collection of custom data that will be added to the payload.</param>
    public void Send(Exception exception, IList<string> tags, IDictionary userCustomData)
    {
      StripAndSend(exception, tags, userCustomData, SynchronousTimeout);
    }

    /// <summary>
    /// Asynchronously transmits a message to Raygun.io.
    /// </summary>
    /// <param name="exception">The exception to deliver.</param>
    public void SendInBackground(Exception exception)
    {
      SendInBackground(exception, null, (IDictionary)null);
    }

    /// <summary>
    /// Asynchronously transmits a message to Raygun.io.
    /// </summary>
    /// <param name="exception">The exception to deliver.</param>
    /// <param name="tags">A list of strings associated with the message.</param>
    public void SendInBackground(Exception exception, IList<string> tags)
    {
      SendInBackground(exception, tags, (IDictionary)null);
    }

    /// <summary>
    /// Asynchronously transmits a message to Raygun.io.
    /// </summary>
    /// <param name="exception">The exception to deliver.</param>
    /// <param name="tags">A list of strings associated with the message.</param>
    /// <param name="userCustomData">A key-value collection of custom data that will be added to the payload.</param>
    public void SendInBackground(Exception exception, IList<string> tags, IDictionary userCustomData)
    {
      ThreadPool.QueueUserWorkItem(c => StripAndSend(exception, tags, userCustomData, 0));
    }

    /// <summary>
    /// Asynchronously transmits a message to Raygun.io.
    /// </summary>
    /// <param name="raygunMessage">The RaygunMessage to send. This needs its OccurredOn property
    /// set to a valid DateTime and as much of the Details property as is available.</param>
    public void SendInBackground(RaygunMessage raygunMessage)
    {
      ThreadPool.QueueUserWorkItem(c => Send(raygunMessage, 0));
    }

    private string DeviceId
    {
      get
      {
        try
        {
          string identifier = NSUserDefaults.StandardUserDefaults.StringForKey ("io.raygun.identifier");
          if (!String.IsNullOrWhiteSpace(identifier))
          {
            return identifier;
          }
        }
        catch { }

        SecRecord query = new SecRecord (SecKind.GenericPassword);
        query.Service = "Mindscape.Raygun";
        query.Account = "RaygunDeviceID";

        NSData deviceId = SecKeyChain.QueryAsData (query);
        if (deviceId == null)
        {
          string id = Guid.NewGuid ().ToString ();
          query.ValueData = NSData.FromString (id);
          SecStatusCode code = SecKeyChain.Add (query);
          if (code != SecStatusCode.Success && code != SecStatusCode.DuplicateItem)
          {
            System.Diagnostics.Debug.WriteLine (string.Format ("Could not save device ID. Security status code: {0}", code));
            return null;
          }

          return id;
        }
        else
        {
          return deviceId.ToString ();
        }
      }
    }

    private static RaygunClient _client;

    /// <summary>
    /// Gets the <see cref="RaygunClient"/> created by the Attach method.
    /// </summary>
    public static RaygunClient Current
    {
      get { return _client; }
    }

    [DllImport ("libc")]
    private static extern int sigaction (Signal sig, IntPtr act, IntPtr oact);

    enum Signal {
      SIGBUS = 10,
      SIGSEGV = 11
    }

    private const string StackTraceDirectory = "stacktraces";
    private Mindscape.Raygun4Net.Xamarin.iOS.Raygun _reporter;

    /// <summary>
    /// Causes Raygun to listen to and send all unhandled exceptions and unobserved task exceptions.
    /// </summary>
    /// <param name="apiKey">Your app api key.</param>
    public static void Attach(string apiKey)
    {
      Attach(apiKey, null);
    }

    /// <summary>
    /// Causes Raygun to listen to and send all unhandled exceptions and unobserved task exceptions.
    /// </summary>
    /// <param name="apiKey">Your app api key.</param>
    /// <param name="canReportNativeErrors">Whether or not to listen to and report native exceptions.</param>
    /// <param name="hijackNativeSignals">When true, this solves the issue where null reference exceptions inside try/catch blocks crash the app, but when false, additional native errors can be reported.</param>
    public static void Attach(string apiKey, bool canReportNativeErrors, bool hijackNativeSignals)
    {
      Attach (apiKey, null, canReportNativeErrors, hijackNativeSignals);
    }

    /// <summary>
    /// Causes Raygun to listen to and send all unhandled exceptions and unobserved task exceptions.
    /// </summary>
    /// <param name="apiKey">Your app api key.</param>
    /// <param name="user">An identity string for tracking affected users.</param>
    public static void Attach(string apiKey, string user)
    {
      Attach (apiKey, user, false, true);
    }

    /// <summary>
    /// Causes Raygun to listen to and send all unhandled exceptions and unobserved task exceptions.
    /// </summary>
    /// <param name="apiKey">Your app api key.</param>
    /// <param name="user">An identity string for tracking affected users.</param>
    /// <param name="canReportNativeErrors">Whether or not to listen to and report native exceptions.</param>
    /// <param name="hijackNativeSignals">When true, this solves the issue where null reference exceptions inside try/catch blocks crash the app, but when false, additional native errors can be reported.</param>
    public static void Attach(string apiKey, string user, bool canReportNativeErrors, bool hijackNativeSignals)
    {
      Detach();

      if(_client == null) {
        _client = new RaygunClient(apiKey);
      }
      AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
      TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

      if (canReportNativeErrors)
      {
        PopulateCrashReportDirectoryStructure();

        if (hijackNativeSignals)
        {
          IntPtr sigbus = Marshal.AllocHGlobal (512);
          IntPtr sigsegv = Marshal.AllocHGlobal (512);

          // Store Mono SIGSEGV and SIGBUS handlers
          sigaction (Signal.SIGBUS, IntPtr.Zero, sigbus);
          sigaction (Signal.SIGSEGV, IntPtr.Zero, sigsegv);

          _client._reporter = Mindscape.Raygun4Net.Xamarin.iOS.Raygun.SharedReporterWithApiKey (apiKey);

          // Restore Mono SIGSEGV and SIGBUS handlers
          sigaction (Signal.SIGBUS, sigbus, IntPtr.Zero);
          sigaction (Signal.SIGSEGV, sigsegv, IntPtr.Zero);

          Marshal.FreeHGlobal (sigbus);
          Marshal.FreeHGlobal (sigsegv);
        }
        else
        {
          _client._reporter = Mindscape.Raygun4Net.Xamarin.iOS.Raygun.SharedReporterWithApiKey (apiKey);
        }
      }

      _client.User = user; // Set this last so that it can be passed to the native reporter.
	  
      string deviceId = _client.DeviceId;	  
      if (user == null && _client._reporter != null && !String.IsNullOrWhiteSpace(deviceId))
      {
        _client._reporter.Identify(deviceId);
      }
    }

    /// <summary>
    /// Initializes the static RaygunClient with the given Raygun api key.
    /// </summary>
    /// <param name="apiKey">Your Raygun api key for this application.</param>
    /// <returns>The RaygunClient to chain other methods.</returns>
    public static RaygunClient Initialize(string apiKey)
    {
      if(_client == null) {
        _client = new RaygunClient(apiKey);
      }
      return _client;
    }

    /// <summary>
    /// Causes Raygun to listen to and send all unhandled exceptions and unobserved task exceptions.
    /// Native iOS exception reporting is not enabled with this method, an overload is available to do so.
    /// </summary>
    /// <returns>The RaygunClient to chain other methods.</returns>
    public RaygunClient AttachCrashReporting()
    {
      return AttachCrashReporting(false, false);
    }

    /// <summary>
    /// Causes Raygun to listen to and send all unhandled exceptions and unobserved task exceptions.
    /// </summary>
    /// <param name="canReportNativeErrors">Whether or not to listen to and report native exceptions.</param>
    /// <param name="hijackNativeSignals">When true, this solves the issue where null reference exceptions inside try/catch blocks crash the app, but when false, additional native errors can be reported.</param>
    /// <returns>The RaygunClient to chain other methods.</returns>
    public RaygunClient AttachCrashReporting(bool canReportNativeErrors, bool hijackNativeSignals)
    {
      RaygunClient.DetachCrashReporting();

      AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
      TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

      if (canReportNativeErrors)
      {
        PopulateCrashReportDirectoryStructure();

        if (hijackNativeSignals)
        {
          IntPtr sigbus = Marshal.AllocHGlobal (512);
          IntPtr sigsegv = Marshal.AllocHGlobal (512);

          // Store Mono SIGSEGV and SIGBUS handlers
          sigaction (Signal.SIGBUS, IntPtr.Zero, sigbus);
          sigaction (Signal.SIGSEGV, IntPtr.Zero, sigsegv);

          _reporter = Mindscape.Raygun4Net.Xamarin.iOS.Raygun.SharedReporterWithApiKey (_apiKey);

          // Restore Mono SIGSEGV and SIGBUS handlers
          sigaction (Signal.SIGBUS, sigbus, IntPtr.Zero);
          sigaction (Signal.SIGSEGV, sigsegv, IntPtr.Zero);

          Marshal.FreeHGlobal (sigbus);
          Marshal.FreeHGlobal (sigsegv);
        }
        else
        {
          _reporter = Mindscape.Raygun4Net.Xamarin.iOS.Raygun.SharedReporterWithApiKey (_apiKey);
        }
      }
      return this;
    }

    /// <summary>
    /// Causes Raygun to automatically send session and view events for Raygun Pulse.
    /// </summary>
    /// <returns>The RaygunClient to chain other methods.</returns>
    public RaygunClient AttachPulse()
    {
      Pulse.Attach(this);
      return this;
    }

    /// <summary>
    /// Detaches Raygun from listening to unhandled exceptions and unobserved task exceptions.
    /// </summary>
    public static void Detach()
    {
      AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
      TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
    }

    /// <summary>
    /// Detaches Raygun from listening to unhandled exceptions and unobserved task exceptions.
    /// </summary>
    public static void DetachCrashReporting()
    {
      AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
      TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
    }

    /// <summary>
    /// Detaches Raygun from automatically sending session and view events to Raygun Pulse.
    /// </summary>
    public static void DetachPulse()
    {
      Pulse.Detach();
    }

    private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
      if (e.Exception != null)
      {
        _client.Send(e.Exception);
        if (_client._reporter != null)
        {
          WriteExceptionInformation (_client._reporter.NextReportUUID, e.Exception);
        }
      }
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
      if (e.ExceptionObject is Exception)
      {
        _client.Send(e.ExceptionObject as Exception, new List<string>(){ "UnhandledException" });
        if (_client._reporter != null)
        {
          WriteExceptionInformation (_client._reporter.NextReportUUID, e.ExceptionObject as Exception);
        }
        Pulse.SendRemainingViews();
      }
    }

    private static string StackTracePath
    {
      get
      {
        string documents = NSFileManager.DefaultManager.GetUrls(NSSearchPathDirectory.DocumentDirectory, NSSearchPathDomain.User)[0].Path;
        var path = Path.Combine (documents, "..", "Library", "Caches", StackTraceDirectory);
        return path;
      }
    }

    private static void PopulateCrashReportDirectoryStructure()
    {
      try
      {
        Directory.CreateDirectory(StackTracePath);

        // Write client info to a file to be picked up by the native reporter:
        var clientInfoPath = Path.GetFullPath(Path.Combine(StackTracePath, "RaygunClientInfo"));
        var clientMessage = new RaygunClientMessage();
        string clientInfo = String.Format("{0}\n{1}\n{2}", clientMessage.Version, clientMessage.Name, clientMessage.ClientUrl);
        File.WriteAllText(clientInfoPath, clientInfo);
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine (string.Format ("Failed to populate crash report directory structure: {0}", ex.Message));
      }
    }

    private static void WriteExceptionInformation(string identifier, Exception exception)
    {
      try
      {
        if (exception == null)
        {
          return;
        }

        var path = Path.GetFullPath(Path.Combine(StackTracePath, string.Format ("{0}", identifier)));

        var exceptionType = exception.GetType ();
        string message = exceptionType.Name + ": " + exception.Message;

        File.WriteAllText(path, string.Join(Environment.NewLine, exceptionType.FullName, message, exception.StackTrace));
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine (string.Format ("Failed to write managed exception information: {0}", ex.Message));
      }
    }

    protected RaygunMessage BuildMessage(Exception exception, IList<string> tags, IDictionary userCustomData)
    {
      string machineName = null;
      try
      {
        machineName = UIDevice.CurrentDevice.Name;
      }
      catch (Exception e)
      {
        System.Diagnostics.Debug.WriteLine("Exception getting device name {0}", e.Message);
      }

      var message = RaygunMessageBuilder.New
        .SetEnvironmentDetails()
        .SetMachineName(machineName)
        .SetExceptionDetails(exception)
        .SetClientDetails()
        .SetVersion(ApplicationVersion)
        .SetTags(tags)
        .SetUserCustomData(userCustomData)
        .SetUser(BuildRaygunIdentifierMessage(machineName))
        .Build();

      var customGroupingKey = OnCustomGroupingKey(exception, message);
      if(string.IsNullOrEmpty(customGroupingKey) == false)
      {
        message.Details.GroupingKey = customGroupingKey;
      }

      return message;
    }

    private RaygunIdentifierMessage BuildRaygunIdentifierMessage(string machineName)
    {
      RaygunIdentifierMessage message = UserInfo;
      string deviceId = DeviceId;

      if (message == null || message.Identifier == null) {
        if (!String.IsNullOrWhiteSpace (User)) {
          message = new RaygunIdentifierMessage (User);
        } else if(!String.IsNullOrWhiteSpace (deviceId)){
          message = new RaygunIdentifierMessage (deviceId) {
            IsAnonymous = true,
            FullName = machineName,
            UUID = deviceId
          };
        }
      }

      if (message != null && message.UUID == null) {
        message.UUID = deviceId;
      }

      return message;
    }

    private void StripAndSend(Exception exception, IList<string> tags, IDictionary userCustomData, int timeout)
    {
      foreach (Exception e in StripWrapperExceptions(exception))
      {
        Send(BuildMessage(e, tags, userCustomData), timeout);
      }
    }

    protected IEnumerable<Exception> StripWrapperExceptions(Exception exception)
    {
      if (exception != null && _wrapperExceptions.Any(wrapperException => exception.GetType() == wrapperException && exception.InnerException != null))
      {
        System.AggregateException aggregate = exception as System.AggregateException;
        if (aggregate != null)
        {
          foreach (Exception e in aggregate.InnerExceptions)
          {
            foreach (Exception ex in StripWrapperExceptions(e))
            {
              yield return ex;
            }
          }
        }
        else
        {
          foreach (Exception e in StripWrapperExceptions(exception.InnerException))
          {
            yield return e;
          }
        }
      }
      else
      {
        yield return exception;
      }
    }

    /// <summary>
    /// Posts a RaygunMessage to the Raygun.io api endpoint.
    /// </summary>
    /// <param name="raygunMessage">The RaygunMessage to send. This needs its OccurredOn property
    /// set to a valid DateTime and as much of the Details property as is available.</param>
    public override void Send(RaygunMessage raygunMessage)
    {
      Send (raygunMessage, SynchronousTimeout);
    }

    private void Send(RaygunMessage raygunMessage, int timeout)
    {
      if (ValidateApiKey())
      {
        bool canSend = OnSendingMessage(raygunMessage);
        if (canSend)
        {
          string message = null;
          try
          {
            message = SimpleJson.SerializeObject(raygunMessage);
          }
          catch (Exception ex)
          {
            System.Diagnostics.Debug.WriteLine (string.Format ("Error serializing message {0}", ex.Message));
          }

          if (message != null)
          {
            try
            {
              SaveMessage(message);
            }
            catch (Exception ex)
            {
              System.Diagnostics.Debug.WriteLine (string.Format ("Error saving Exception to device {0}", ex.Message));
              if (HasInternetConnection)
              {
                SendMessage(message, timeout);
              }
            }

            // In the case of sending messages during a crash, only send stored messages if there are 2 or less.
            // This is to prevent keeping the app open for a long time while it crashes.
            if (HasInternetConnection && GetStoredMessageCount() <= 2)
            {
              SendStoredMessages(timeout);
            }
          }
        }
      }
    }

    private string _sessionId;

    internal void SendPulseSessionEventNow(RaygunPulseSessionEventType eventType)
    {
      if (eventType == RaygunPulseSessionEventType.SessionStart)
      {
        _sessionId = Guid.NewGuid().ToString();
      }
      SendPulseSessionEventCore(eventType);
    }

    /// <summary>
    /// Sends a Pulse session event to Raygun. The message is sent on a background thread.
    /// </summary>
    /// <param name="eventType">The type of session event that occurred.</param>
    internal void SendPulseSessionEvent(RaygunPulseSessionEventType eventType)
    {
      if (eventType == RaygunPulseSessionEventType.SessionStart)
      {
        _sessionId = Guid.NewGuid().ToString();
      }
      ThreadPool.QueueUserWorkItem(c => SendPulseSessionEventCore(eventType));
    }

    private void SendPulseSessionEventCore(RaygunPulseSessionEventType eventType)
    {
      RaygunPulseMessage message = new RaygunPulseMessage();
      RaygunPulseDataMessage data = new RaygunPulseDataMessage();
      data.Timestamp = DateTime.UtcNow;
      data.Version = GetVersion();

      data.OS = UIDevice.CurrentDevice.SystemName;
      data.OSVersion = UIDevice.CurrentDevice.SystemVersion;
      data.Platform = Mindscape.Raygun4Net.Builders.RaygunEnvironmentMessageBuilder.GetStringSysCtl("hw.machine");

      string machineName = null;
      try
      {
        machineName = UIDevice.CurrentDevice.Name;
      }
      catch (Exception e)
      {
        System.Diagnostics.Debug.WriteLine("Exception getting device name {0}", e.Message);
      }
      data.User = BuildRaygunIdentifierMessage(machineName);
      message.EventData = new [] { data };
      switch(eventType) {
      case RaygunPulseSessionEventType.SessionStart:
        data.Type = "session_start";
        break;
      case RaygunPulseSessionEventType.SessionEnd:
        data.Type = "session_end";
        break;
      }
      data.SessionId = _sessionId;
      Send(message);
    }

    internal void SendPulseTimingEventNow(RaygunPulseEventType eventType, string name, long milliseconds)
    {
      SendPulseTimingEventCore(eventType, name, milliseconds);
    }

    private PulseEventBatch _activeBatch;

    /// <summary>
    /// Sends a pulse timing event to Raygun. The message is sent on a background thread.
    /// </summary>
    /// <param name="eventType">The type of event that occurred.</param>
    /// <param name="name">The name of the event resource such as the view name or URL of a network call.</param>
    /// <param name="milliseconds">The duration of the event in milliseconds.</param>
    public void SendPulseTimingEvent(RaygunPulseEventType eventType, string name, long milliseconds)
    {
      if (_activeBatch == null) {
        _activeBatch = new PulseEventBatch (this);
      }

      if (_activeBatch != null && !_activeBatch.IsLocked) {
        if (_sessionId == null) {
          SendPulseSessionEvent (RaygunPulseSessionEventType.SessionStart);
        }
        PendingEvent pendingEvent = new PendingEvent (eventType, name, milliseconds, _sessionId);
        _activeBatch.Add (pendingEvent);
      } else {
        ThreadPool.QueueUserWorkItem (c => SendPulseTimingEventCore (eventType, name, milliseconds));
      }
    }

    internal void Send (PulseEventBatch batch)
    {
      ThreadPool.QueueUserWorkItem (c => SendCore(batch));
      _activeBatch = null;
    }

    private void SendCore (PulseEventBatch batch)
    {
      if (_sessionId == null) {
        SendPulseSessionEvent (RaygunPulseSessionEventType.SessionStart);
      }

      string version = GetVersion ();
      string os = UIDevice.CurrentDevice.SystemName;
      string osVersion = UIDevice.CurrentDevice.SystemVersion;
      string platform = Mindscape.Raygun4Net.Builders.RaygunEnvironmentMessageBuilder.GetStringSysCtl ("hw.machine");

      string machineName = null;
      try {
        machineName = UIDevice.CurrentDevice.Name;
      } catch (Exception e) {
        System.Diagnostics.Debug.WriteLine ("Exception getting device name {0}", e.Message);
      }

      RaygunIdentifierMessage user = BuildRaygunIdentifierMessage (machineName);

      RaygunPulseMessage message = new RaygunPulseMessage ();

      Debug.WriteLine ("BatchSize: " + batch.PendingEventCount);

      RaygunPulseDataMessage [] eventMessages = new RaygunPulseDataMessage[batch.PendingEventCount];
      int index = 0;
      foreach (PendingEvent pendingEvent in batch.PendingEvents) {

        RaygunPulseDataMessage dataMessage = new RaygunPulseDataMessage ();
        dataMessage.SessionId = pendingEvent.SessionId;
        dataMessage.Timestamp = pendingEvent.Timestamp;
        dataMessage.Version = version;
        dataMessage.OS = os;
        dataMessage.OSVersion = osVersion;
        dataMessage.Platform = platform;
        dataMessage.Type = "mobile_event_timing";
        dataMessage.User = user;

        string type = pendingEvent.EventType == RaygunPulseEventType.ViewLoaded ? "p" : "n";

        RaygunPulseData data = new RaygunPulseData () { Name = pendingEvent.Name, Timing = new RaygunPulseTimingMessage () { Type = type, Duration = pendingEvent.Duration } };
        RaygunPulseData [] dataArray = { data };
        string dataStr = SimpleJson.SerializeObject (dataArray);
        dataMessage.Data = dataStr;

        eventMessages [index] = dataMessage;
        index++;
      }
      message.EventData = eventMessages;

      Send (message);
    }

    private void SendPulseTimingEventCore(RaygunPulseEventType eventType, string name, long milliseconds)
    {
      if(_sessionId == null) {
        SendPulseSessionEvent(RaygunPulseSessionEventType.SessionStart);
      }

      RaygunPulseMessage message = new RaygunPulseMessage();
      RaygunPulseDataMessage dataMessage = new RaygunPulseDataMessage();
      dataMessage.SessionId = _sessionId;
      dataMessage.Timestamp = DateTime.UtcNow - TimeSpan.FromMilliseconds(milliseconds);
      dataMessage.Version = GetVersion();
      dataMessage.OS = UIDevice.CurrentDevice.SystemName;
      dataMessage.OSVersion = UIDevice.CurrentDevice.SystemVersion;
      dataMessage.Platform = Mindscape.Raygun4Net.Builders.RaygunEnvironmentMessageBuilder.GetStringSysCtl("hw.machine");
      dataMessage.Type = "mobile_event_timing";

      string machineName = null;
      try
      {
        machineName = UIDevice.CurrentDevice.Name;
      }
      catch (Exception e)
      {
        System.Diagnostics.Debug.WriteLine("Exception getting device name {0}", e.Message);
      }

      dataMessage.User = BuildRaygunIdentifierMessage(machineName);

      string type = eventType == RaygunPulseEventType.ViewLoaded ? "p" : "n";

      RaygunPulseData data = new RaygunPulseData(){ Name = name, Timing = new RaygunPulseTimingMessage() { Type = type, Duration = milliseconds } };
      RaygunPulseData[] dataArray = { data };
      string dataStr = SimpleJson.SerializeObject(dataArray);
      dataMessage.Data = dataStr;

      message.EventData = new [] { dataMessage };

      Send(message);
    }

    private string GetVersion()
    {
      string version = ApplicationVersion;
      if (String.IsNullOrWhiteSpace(version))
      {
        try
        {
          string versionNumber = NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString").ToString();
          string buildNumber = NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleVersion").ToString();
          version = String.Format("{0} ({1})", versionNumber, buildNumber);
        }
        catch (Exception ex)
        {
          System.Diagnostics.Trace.WriteLine("Error retieving bundle version {0}", ex.Message);
        }
      }

      if (String.IsNullOrWhiteSpace(version))
      {
        version = "Not supplied";
      }

      return version;
    }

    private void Send(RaygunPulseMessage raygunPulseMessage)
    {
      if (ValidateApiKey())
      {
        string message = null;
        try
        {
          message = SimpleJson.SerializeObject(raygunPulseMessage);
        }
        catch (Exception ex) {
          System.Diagnostics.Debug.WriteLine(string.Format("Error serializing message {0}", ex.Message));
        }

        if (message != null)
        {
          SendPulseMessage(message);
        }
      }
    }

    private bool SendMessage (string message, int timeout)
    {
      using (var client = new TimeoutWebClient(timeout))
      {
        client.Headers.Add("X-ApiKey", _apiKey);
        client.Headers.Add("content-type", "application/json; charset=utf-8");
        client.Encoding = System.Text.Encoding.UTF8;

        try
        {
          client.UploadString(RaygunSettings.Settings.ApiEndpoint, message);
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine(string.Format("Error Logging Exception to Raygun.io {0}", ex.Message));
          return false;
        }
      }
      return true;
    }

    private bool SendPulseMessage(string message)
    {
      using (var client = new WebClient())
      {
        client.Headers.Add("X-ApiKey", _apiKey);
        client.Headers.Add("content-type", "application/json; charset=utf-8");
        client.Encoding = System.Text.Encoding.UTF8;

        try
        {
          client.UploadString(RaygunSettings.Settings.PulseEndpoint, message);
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine(string.Format("Error Logging Pulse message to Raygun.io {0}", ex.Message));
          return false;
        }
      }
      return true;
    }

    private bool HasInternetConnection
    {
      get
      {
        using (NetworkReachability reachability = new NetworkReachability("raygun.io"))
        {
          NetworkReachabilityFlags flags;
          if (reachability.TryGetFlags(out flags))
          {
            bool isReachable = (flags & NetworkReachabilityFlags.Reachable) != 0;
            bool noConnectionRequired = (flags & NetworkReachabilityFlags.ConnectionRequired) == 0;
            if ((flags & NetworkReachabilityFlags.IsWWAN) != 0)
            {
              noConnectionRequired = true;
            }
            return isReachable && noConnectionRequired;
          }
        }
        return false;
      }
    }

    private void SendStoredMessages(int timeout)
    {
      if (HasInternetConnection)
      {
        try
        {
          using (IsolatedStorageFile isolatedStorage = IsolatedStorageFile.GetUserStoreForApplication())
          {
            if (isolatedStorage.DirectoryExists("RaygunIO"))
            {
              string[] fileNames = isolatedStorage.GetFileNames("RaygunIO\\*.txt");
              foreach (string name in fileNames)
              {
                IsolatedStorageFileStream isoFileStream = isolatedStorage.OpenFile(name, FileMode.Open);
                using (StreamReader reader = new StreamReader(isoFileStream))
                {
                  string text = reader.ReadToEnd();
                  bool success = SendMessage(text, timeout);
                  // If just one message fails to send, then don't delete the message, and don't attempt sending anymore until later.
                  if (!success)
                  {
                    return;
                  }
                  System.Diagnostics.Debug.WriteLine("Sent " + name);
                }
                isolatedStorage.DeleteFile(name);
              }
              if (isolatedStorage.GetFileNames("RaygunIO\\*.txt").Length == 0)
              {
                System.Diagnostics.Debug.WriteLine("Successfully sent all pending messages");
              }
              isolatedStorage.DeleteDirectory("RaygunIO");
            }
          }
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine(string.Format("Error sending stored messages to Raygun.io {0}", ex.Message));
        }
      }
    }

    private int GetStoredMessageCount()
    {
      try
      {
        using (IsolatedStorageFile isolatedStorage = IsolatedStorageFile.GetUserStoreForApplication())
        {
          if (isolatedStorage.DirectoryExists("RaygunIO"))
          {
            string[] fileNames = isolatedStorage.GetFileNames("RaygunIO\\*.txt");
            return fileNames.Length;
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine(string.Format("Error getting stored message count: {0}", ex.Message));
      }
      return 0;
    }

    private void SaveMessage(string message)
    {
      try
      {
        using (IsolatedStorageFile isolatedStorage = IsolatedStorageFile.GetUserStoreForApplication())
        {
          if (!isolatedStorage.DirectoryExists("RaygunIO"))
          {
            isolatedStorage.CreateDirectory("RaygunIO");
          }
          int number = 1;
          while (true)
          {
            bool exists = isolatedStorage.FileExists("RaygunIO\\RaygunErrorMessage" + number + ".txt");
            if (!exists)
            {
              string nextFileName = "RaygunIO\\RaygunErrorMessage" + (number + 1) + ".txt";
              exists = isolatedStorage.FileExists(nextFileName);
              if (exists)
              {
                isolatedStorage.DeleteFile(nextFileName);
              }
              break;
            }
            number++;
          }
          if (number == 11)
          {
            string firstFileName = "RaygunIO\\RaygunErrorMessage1.txt";
            if (isolatedStorage.FileExists(firstFileName))
            {
              isolatedStorage.DeleteFile(firstFileName);
            }
          }
          using (IsolatedStorageFileStream isoStream = new IsolatedStorageFileStream("RaygunIO\\RaygunErrorMessage" + number + ".txt", FileMode.OpenOrCreate, FileAccess.Write, isolatedStorage))
          {
            using (StreamWriter writer = new StreamWriter(isoStream, Encoding.Unicode))
            {
              writer.Write(message);
              writer.Flush();
              writer.Close();
            }
          }
          System.Diagnostics.Debug.WriteLine("Saved message: " + "RaygunErrorMessage" + number + ".txt");
          System.Diagnostics.Debug.WriteLine("File Count: " + isolatedStorage.GetFileNames("RaygunIO\\*.txt").Length);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine(string.Format("Error saving message to isolated storage {0}", ex.Message));
      }
    }

    private class TimeoutWebClient : WebClient
    {
      private readonly int _timeout;

      public TimeoutWebClient(int timeout)
      {
        _timeout = timeout;
      }

      protected override WebRequest GetWebRequest(Uri address)
      {
        WebRequest request = base.GetWebRequest(address);
        if (_timeout > 0)
        {
          request.Timeout = _timeout;
        }
        return request;
      }
    }
  }
}
