using System;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NSIRDriver.Logging;
namespace Colt3.IngersollRand {
	public class IngersollRandController : IDisposable {
		#region delegates
		public delegate void ProcessEorData(string[] fields);
		public delegate void DisplayCommStatus(string remoteEndPoint, bool up);
		#endregion
		#region fields
		DisplayCommStatus _DisplayCommStatus;
		IColtLogger _logger1 = null;
		NetworkStream _clientStream = null;
		ProcessEorData _eorDataProcessor;
		TcpClient _tcpClient = null;
		const int BUFF_SIZE = 1024;
		int _port;
		public static bool verboseLogging = true;
		readonly object _LockConnection = new object();
		string _eorData = string.Empty;
		string _host = string.Empty;
		ManualResetEvent _mreExecuting=new ManualResetEvent(false);
		#endregion
		#region ctor
		public IngersollRandController() { }
		#endregion
		#region methods
		public void close() {
			try {
				if (_tcpClient != null) {
					_DisplayCommStatus(string.Format("{0}:{1}", _host, _port), false);
					_tcpClient.Close();
					_tcpClient = null;
				}
				if (_clientStream != null) {
					_clientStream.Close();
					_clientStream = null;
				}
			} catch (Exception ex) {
				//_logger.log(MethodBase.GetCurrentMethod(), ex);
				logError(MethodBase.GetCurrentMethod(), ex);
			}
		}

		#region log-methods
		void logDebug(MethodBase mb, string msg) { log(ColtLogLevel.Debug, mb, msg); }
		void logInfo(MethodBase mb, string msg) { log(ColtLogLevel.Info, mb, msg); }
		void logWarning(MethodBase mb, string msg) {
			log(ColtLogLevel.Warning, mb, msg);
		}

		void logError(MethodBase mb, string msg) { log(ColtLogLevel.Error, mb, msg); }

		void logError(MethodBase mb, Exception ex) {
			if (_logger1!=null)
				_logger1.log(mb, ex);
			else
				NSIRDriver.Utility.logger.log(mb, ex);
		}

		void log(ColtLogLevel level, MethodBase mb, string msg) {
			(_logger1==null ? NSIRDriver.Utility.logger : _logger1).log(level, mb, msg);
		}

		#endregion
		public bool initialize(string host, int port, IColtLogger alogger, ProcessEorData processEorData, DisplayCommStatus displayCommStatus) {
			try {
				this._logger1 = alogger;
				_host = host;
				_port = port;
				_eorDataProcessor = processEorData;
				_DisplayCommStatus = displayCommStatus;
				if (verboseLogging)
					logDebug(MethodBase.GetCurrentMethod(), "Host:" + host + " Port:" + port);
				//_logger.log(ColtLogLevel.Debug, MethodBase.GetCurrentMethod(), "Host:" + host + " Port:" + port);
				_taskConnection=Task.Run(() => {
					controllerConnectionThread();
				});
				_taskRead=Task.Run(() => {
					controllerReadThread();
				});
				return true;
			} catch (Exception ex) {
				logError(MethodBase.GetCurrentMethod(), ex);
				//_logger.log(MethodBase.GetCurrentMethod(), ex);
				return false;
			}
		}

		#region thread-methods
		void controllerConnectionThread() {
			bool exitThread=false;
			try {
				//if (verboseLogging)
				logDebug(MethodBase.GetCurrentMethod(), "starting");
				while (!exitThread) {
					try {
						if (_mreExecuting.WaitOne(100))
							exitThread=true;
						if (exitThread)
							break;
						lock (_LockConnection) {
							if (_tcpClient == null) {
								if (verboseLogging)
									logDebug(MethodBase.GetCurrentMethod(), ":  Constructing Tcp Client at " + _host + ", " + _port);
								_tcpClient = new TcpClient(_host, _port) {
									NoDelay = true
								};
								_clientStream = _tcpClient.GetStream();
								if (_DisplayCommStatus!=null)
								_DisplayCommStatus(string.Format("{0}:{1}", _host, _port), true);
								else
								logWarning(MethodBase.GetCurrentMethod(), "comm-status delegate does not exist: Host="+_host+", Port="+_port+".");
								if (verboseLogging)
									logDebug(MethodBase.GetCurrentMethod(), ":  Connection Successful");
							}
						}
					} catch (Exception ex) {
						_DisplayCommStatus(string.Format("{0}:{1}", _host, _port), false);
						lock (_LockConnection)
							close();
						logError(MethodBase.GetCurrentMethod(), ex);
						Thread.Sleep(1000 * 5);
					}
				}
			} catch (Exception ex) {
				logError(MethodBase.GetCurrentMethod(), ex);
			}
			logDebug(MethodBase.GetCurrentMethod(), "ending");
		}

	
		void controllerReadThread() {
			byte[] buffer = new byte[BUFF_SIZE];
			int bytesRead;
			bool exitThread=false;

			try {
				logDebug(MethodBase.GetCurrentMethod(), "starting");
				_eorData = string.Empty;
				//if (verboseLogging)
				//	_logger.log(ColtLogLevel.Info, MethodBase.GetCurrentMethod(), ": read loop");
				while (!exitThread) {
					try {
						if (_mreExecuting.WaitOne(100))
							exitThread=true;
						//_logger.log(ColtLogLevel.Debug, MethodBase.GetCurrentMethod(), "DID waitone");
						if (exitThread)
							break;
						lock (_LockConnection) {
							if (_clientStream == null) {
								logInfo(MethodBase.GetCurrentMethod(), "no client-stream yet.");
								Thread.Sleep(1000 * 5);
								continue;
							}
						}
						if (!_clientStream.DataAvailable) {
							logDebug(MethodBase.GetCurrentMethod(), "no data yet.");

							Thread.Sleep(1000);
							continue;
						}
						//if (verboseLogging)
						logInfo(MethodBase.GetCurrentMethod(), "have data, so reading...");
						bytesRead = _clientStream.Read(buffer, 0, buffer.Length);
						//if (verboseLogging)
						logInfo(MethodBase.GetCurrentMethod(), ": " + bytesRead + " bytes read.");
						if (bytesRead < 1) {
							lock (_LockConnection) {
								close();
								logError(MethodBase.GetCurrentMethod(), "Read Failed - Connection Must Be Closed - Closing Our End");
							}
							_eorData = string.Empty;
							continue;
						}
						if (verboseLogging)
							logInfo(MethodBase.GetCurrentMethod(), ": converting bytes read to ASCII");
						_eorData += Encoding.ASCII.GetString(buffer, 0, bytesRead);
						if (verboseLogging)
							logInfo(MethodBase.GetCurrentMethod(), ": adding ASCII chars to our string");
						string[] fields = _eorData.ToString().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
						if (fields.Length < 2) {
							if (verboseLogging)
								logInfo(MethodBase.GetCurrentMethod(), ": [" + _eorData + "] only has " + fields.Length + " fields.");
							continue;
						}
						if (verboseLogging)
							logInfo(MethodBase.GetCurrentMethod(), ":  parsed the string '" + _eorData + "' into " + fields.Length + " fields");
						_eorData = string.Empty;
						_eorDataProcessor.BeginInvoke(fields, null, null);
					} catch (Exception ex) {
						lock (_LockConnection) {
							close();
						}
						Thread.Sleep(1000 * 5);
					}
				}
			} catch (Exception ex) {
				logError(MethodBase.GetCurrentMethod(), ex);
			}
			logDebug(MethodBase.GetCurrentMethod(), "ending");
		}

		#endregion
		#endregion
		#region IDisposable Support
		bool _isDisposed = false;
		Task _taskConnection;
		Task _taskRead;

		protected virtual void Dispose(bool disposing) {
			if (!_isDisposed) {
				if (disposing) {
					logDebug(MethodBase.GetCurrentMethod(), "killing tasks");
					_mreExecuting.Set();
					Task.WaitAll(_taskConnection, _taskRead);
					logDebug(MethodBase.GetCurrentMethod(), "tasks have exited.");
				}
				_isDisposed = true;
			}
		}
		public void Dispose() {
			Dispose(true);
		}
		#endregion
	}
}