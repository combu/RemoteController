using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Threading;
using System.Drawing;
using System.IO;
using System.Text;
using SuperWebSocket;
using SuperSocket.SocketBase;
using SuperSocket.SocketBase.Config;
using System.ComponentModel;

namespace Server
{
	public static class Extensions
	{
		public static object Get(this Dictionary<string,object> obj,string key)
		{
			object tmp=null;
			obj.TryGetValue(key,out tmp);
			return tmp;
		}
	}

	public class UserEventArgs:EventArgs
	{
		public string UserName{get;private set;}

		public UserEventArgs(string userName)
		{
			UserName=userName;
		}
	}

	public class MessageReceivedEventArgs:EventArgs
	{
		public string UserName{get;private set;}
		public string Message{get;private set;}
		
		public MessageReceivedEventArgs(string userName,string message)
		{
			UserName=userName;
			Message=message;
		}
	}

	public class ScreenShotReceivedEventArgs:EventArgs
	{
		public string UserName{get;private set;}
		public Bitmap Image{get;private set;}

		public ScreenShotReceivedEventArgs(string userName,byte[] fileData)
		{
			UserName=userName;
			if(fileData==null) Image=null;
			else using(var stream=new MemoryStream(fileData)) Image=new Bitmap(stream);
		}
	}

	public class Session
	{
		public WebSocketSession SocketSession{get;private set;}
		public Color Color{get;private set;}
		public string MachineName{get;private set;}

		public Session(WebSocketSession session,Color color,string machineName)
		{
			SocketSession=session;
			Color=color;
			MachineName=machineName;
		}
	}

	public enum JoinRequestResultReason
	{
		Succeeded=0,NameAlreadyUsed=1,InvalidRequest=2,AlreadyConnected=3,UnknownError=4
	}

	public enum PowerEventKind
	{
		Shutdown=0,Reboot=1,Suspend=2,Hibernate=3
	}

	public class MessageServer
	{
		WebSocketServer server;
		const int port=7345;
		Dictionary<string,Session> sessions;
		Json.Json json;
		ISynchronizeInvoke syncObj;

		public event Action<WebSocketSession,UserEventArgs> UserAdded;
		public event Action<UserEventArgs> UserRemoved;
		public event Action<WebSocketSession,MessageReceivedEventArgs> MessageReceived;
		public event Action<WebSocketSession,ScreenShotReceivedEventArgs> ScreenShotReceived;
		public Func<string,Color> GenerateColor{get;set;}
		public string OperatorName{get;private set;}

		public MessageServer(Form owner,Func<string,Color> colorGenerator,string operatorName)
		{
			if(colorGenerator==null) throw new ArgumentNullException();
			GenerateColor=colorGenerator;
			OperatorName=operatorName;
			json=new Json.Json();
			server=new WebSocketServer();
			sessions=new Dictionary<string,Session>();
			syncObj=owner;
			InitServer();
		}

		private void InitServer()
		{
			server=new WebSocketServer();
			var config=new ServerConfig(){
				Ip="Any",
				Port=port,
				KeepAliveInterval=15,
				MaxRequestLength=10485760
			};
			server.NewMessageReceived+=OnNewMessageReceived;
			server.NewDataReceived+=OnNewDataReceived;
			server.SessionClosed+=OnSessionClosed;
			server.Setup(config);
		}

		public void Start()
		{
			server.Start();
		}

		public void Stop()
		{
			server.Stop();
		}

		private string GetNameBySession(WebSocketSession session)
		{
			return sessions.FirstOrDefault(pair=>pair.Value.SocketSession==session).Key;
		}

		private WebSocketSession GetSessionFromName(string name)
		{
			return sessions.FirstOrDefault(s=>s.Key==name).Value.SocketSession;
		}

		private WebSocketSession[] GetSessionsFromNames(string[] names)
		{
			return (from session in sessions
					where names.Contains(session.Key)
					select session.Value.SocketSession).ToArray();
		}

		private void ProcessJoinRequest(WebSocketSession session,string name,string machineName)
		{
			var result=false;
			var reason=JoinRequestResultReason.UnknownError;
			if(sessions.Any(pair=>pair.Value.SocketSession==session)) reason=JoinRequestResultReason.AlreadyConnected;
			else if(name==null||name=="") reason=JoinRequestResultReason.InvalidRequest;
			else if(sessions.Any(pair=>pair.Key==name)) reason=JoinRequestResultReason.NameAlreadyUsed;
			else{
				reason=JoinRequestResultReason.Succeeded;
				result=true;
			}
			var obj=new Dictionary<string,object>(){
				{"type","join"},{"result",result},{"reason",(int)reason}
			};
			session.Send(json.ToString(obj));
			if(result){
				SendServerInfo(session,sessions);
				var color=GenerateColor(name);
				sessions.Add(name,new Session(session,color,machineName));
				SendUserAddedNotice(name,color);
				OnUserAdded(session,new UserEventArgs(name));
			}else session.Close();
		}

		private void BroadcastMessage(string name,string text)
		{
			var obj=new Dictionary<string,object>(){
				{"type","message"},{"name",name},{"text",text}
			};
			Send(json.ToString(obj),sessions.Select(pair=>pair.Value.SocketSession).ToArray());
		}

		private void ProcessMessageData(WebSocketSession session,string text)
		{
			var name=GetNameBySession(session);
			if(name!=null&&text!=null){
				BroadcastMessage(name,text);
				OnMessageReceived(session,new MessageReceivedEventArgs(name,text));
			}
		}

		private void ProcessLeaveMessage(WebSocketSession session)
		{
			var name=GetNameBySession(session);
			if(name!=null){
				SendUserRemovedNotice(name);
				sessions.Remove(name);
				OnUserRemoved(new UserEventArgs(name));
			}
		}

		private void ProcessMessage(WebSocketSession session,Dictionary<string,object> obj)
		{
			switch(obj.Get("type") as string){
			case "join":
				ProcessJoinRequest(session,obj.Get("name") as string,obj.Get("machinename") as string);
				break;
			case "message":
				ProcessMessageData(session,obj.Get("text") as string);
				break;
			case "leave":
				ProcessLeaveMessage(session);
				break;
			default:
				break;
			}
		}

		private void OnNewMessageReceived(WebSocketSession session,string message)
		{
			var obj=json.Parse(message);
			if(obj is Dictionary<string,object>) ProcessMessage(session,(Dictionary<string,object>)obj);
		}

		private void OnNewDataReceived(WebSocketSession session,byte[] data)
		{
			var name=GetNameBySession(session);
			if(name!=null){
				var header=data.TakeWhile(b=>b!=0).ToArray();
				var type=Encoding.UTF8.GetString(header);
				var body=data.Skip(header.Length+1).ToArray();
				switch(type){
				case "screenshot":
					OnScreenShotReceived(session,new ScreenShotReceivedEventArgs(name,body));
					break;
				default:
					break;
				}
			}
		}

		private void OnSessionClosed(WebSocketSession session,SuperSocket.SocketBase.CloseReason reason)
		{
			if(server.State==ServerState.Running){
				var name=GetNameBySession(session);
				if(name!=null){
					SendUserRemovedNotice(name);
					sessions.Remove(name);
					OnUserRemoved(new UserEventArgs(name));
				}
			}
		}

		private void OnUserAdded(WebSocketSession session,UserEventArgs args)
		{
			if(UserAdded!=null) syncObj.BeginInvoke((Action)(()=>UserAdded(session,args)),null);
		}

		private void OnMessageReceived(WebSocketSession session,MessageReceivedEventArgs args)
		{
			if(MessageReceived!=null) syncObj.BeginInvoke((Action)(()=>MessageReceived(session,args)),null);
		}

		private void OnScreenShotReceived(WebSocketSession session,ScreenShotReceivedEventArgs args)
		{
			if(ScreenShotReceived!=null) syncObj.BeginInvoke((Action)(()=>ScreenShotReceived(session,args)),null);
		}

		private void OnUserRemoved(UserEventArgs args)
		{
			if(UserRemoved!=null) syncObj.BeginInvoke((Action)(()=>UserRemoved(args)),null);
		}

		private void Send(string message,WebSocketSession[] sessions)
		{
			foreach(var session in sessions) if(session.Connected) session.Send(message);
		}

		private void SendServerInfo(WebSocketSession session,Dictionary<string,Session> userList)
		{
			var users=new List<object>();
			userList.ToList().ForEach(p=>{
				users.Add(new Dictionary<string,object>(){
					{"name",p.Key},{"color",p.Value.Color.ToArgb()}
				});
			});
			var obj=new Dictionary<string,object>(){
				{"type","serverinfo"},{"operatorname",OperatorName},{"users",users}
			};
			session.Send(json.ToString(obj));
		}

		private void SendUserAddedNotice(string name,Color color)
		{
			var obj=new Dictionary<string,object>(){
				{"type","adduser"},{"name",name},{"color",color.ToArgb()}
			};
			Send(json.ToString(obj),sessions.Select(pair=>pair.Value.SocketSession).ToArray());
		}

		private void SendUserRemovedNotice(string name)
		{
			var obj=new Dictionary<string,object>(){
				{"type","deluser"},{"name",name}
			};
			Send(json.ToString(obj),sessions.Select(pair=>pair.Value.SocketSession).ToArray());
		}

		public void SendOpenRequest(string resourceName,string[] targets)
		{
			var obj=new Dictionary<string,object>(){
				{"type","open"},{"resourcename",resourceName}
			};
			Send(json.ToString(obj),GetSessionsFromNames(targets));
		}

		public void SendExecuteRequest(string commandName,string commandArgs,string[] targets)
		{
			var obj=new Dictionary<string,object>(){
				{"type","execute"},{"command",commandName},{"arguments",commandArgs}
			};
			Send(json.ToString(obj),GetSessionsFromNames(targets));
		}

		public void SendScreenShotRequest(int width,int height,string[] targets)
		{
			var obj=new Dictionary<string,object>(){
				{"type","screenshot"},{"width",width},{"height",height}
			};
			Send(json.ToString(obj),GetSessionsFromNames(targets));
		}

		public void SendMessage(string text)
		{
			var obj=new Dictionary<string,object>(){
				{"type","message"},{"name",""},{"text",text}
			};
			Send(json.ToString(obj),sessions.Select(pair=>pair.Value.SocketSession).ToArray());
		}

		public void SendPowerEvent(PowerEventKind eventKind,string[] targetMachines)
		{
			var targets=from session in sessions
						where targetMachines.Contains(session.Value.MachineName)
						select session.Key;
			var obj=new Dictionary<string,object>(){
				{"type","power"},{"kind",(int)eventKind}
			};
			Send(json.ToString(obj),GetSessionsFromNames(targets.ToArray()));
		}
	}

	static class Program
	{
		/// <summary>
		/// アプリケーションのメイン エントリ ポイントです。
		/// </summary>
		[STAThread]
		static void Main()
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			var result=false;
			var mutex=new Mutex(true,"RemoteController_Server",out result);
			if(result){
				Application.ApplicationExit+=(o,e)=>mutex.ReleaseMutex();
				Application.Run(new Form1());
			}else MessageBox.Show("既にServerは起動しています。","RemoteController(Server)",MessageBoxButtons.OK,MessageBoxIcon.Warning);
		}
	}
}
