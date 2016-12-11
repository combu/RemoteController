using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using System.Net.NetworkInformation;
using SuperWebSocket;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Server
{
	//ファイル転送機能の実装
	public partial class Form1 : Form
	{
		MessageServer server;
		Color[] colors;
		IPAddress localAddress;
		string nickName="[Server]";
		readonly Size thumbnailSize=new Size(200,150);
		Form3 screenShotListView=null;
		Form4 powerManagerWindow;
		Form5 fileTransferWindow;
		List<string> commandHistory=new List<string>();
		BindingSource userList=new BindingSource();
		const int maxHistoryCount=20;
		int currentHistory=-1;
		CancellationTokenSource cancelTokenSource=new CancellationTokenSource();

		public Form1()
		{
			InitializeComponent();
			Name="";
			server=new MessageServer(this,GenerateColor,nickName);
			server.UserAdded+=OnUserAdded;
			server.UserRemoved+=OnUserRemoved;
			server.MessageReceived+=OnMessageReceived;
			server.ScreenShotReceived+=OnScreenShotReceived;
			colors=GetUserColors();
			localAddress=GetLocalIPAddress()??IPAddress.Any;
			server.Start();
			UpdateTitle();
			userList.DataSource=new List<User>();
			listBox1.DataSource=userList;
			Task.Factory.StartNew(ResponseSearchMessage,cancelTokenSource.Token);
			fileTransferWindow=new Form5();
		}

		private void Form1_FormClosing(object sender,FormClosingEventArgs e)
		{
			cancelTokenSource.Cancel();
			server.UserRemoved-=OnUserRemoved;
			server.Stop();
		}

		private void button1_Click(object sender,EventArgs e)
		{
			if(textBox1.Text!=""){
				var targets=from user in listBox1.SelectedItems.Cast<User>() select user.Name;
				if(targets.Count()==0) targets=from user in userList.Cast<User>() select user.Name;
				if(radioButton1.Checked) server.SendOpenRequest(textBox1.Text,targets.ToArray());
				else if(radioButton2.Checked){
					var splittedCommand=ParseCommand(textBox1.Text);
					server.SendExecuteRequest(splittedCommand[0],splittedCommand[1],targets.ToArray());
				}else if(radioButton3.Checked){
					AppendLog("",nickName+": "+textBox1.Text);
					server.SendMessage(textBox1.Text);
				}
				commandHistory.Insert(0,textBox1.Text);
				if(commandHistory.Count>maxHistoryCount) commandHistory.RemoveAt(20);
				textBox1.Text="";
			}
			currentHistory=-1;
			listBox1.SelectedIndex=-1;
			textBox1.Focus();
		}

		private Color GenerateColor(string name)
		{
			return colors[new Random(name.GetHashCode()).Next(colors.Length)];
		}

		private void OnUserAdded(WebSocketSession session,UserEventArgs e)
		{
			var name=e.UserName;
			var color=GenerateColor(name);
			userList.Add(new User(name,color));
			AppendLog(name,name+"さんが接続しました。");
			if(Application.OpenForms.Cast<Form>().Any(f=>f is Form3)) screenShotListView.AddUser(name);
			UpdateTitle();
		}

		private void OnUserRemoved(UserEventArgs e)
		{
			var name=e.UserName;
			if(Application.OpenForms.Cast<Form>().Any(f=>f.Name==name))
				(Application.OpenForms[name] as Form2).Close();
			AppendLog(name,name+"さんが切断しました。");
			userList.Remove(userList.Cast<User>().First(user=>user.Name==name));
			if(Application.OpenForms.Cast<Form>().Any(f=>f is Form3)) screenShotListView.RemoveUser(name);
			UpdateTitle();
		}

		private void OnMessageReceived(WebSocketSession session,MessageReceivedEventArgs e)
		{
			AppendLog(e.UserName,e.UserName+": "+e.Message);
		}

		private void OnScreenShotReceived(WebSocketSession session,ScreenShotReceivedEventArgs e)
		{
			if(e.Image!=null){
				if(e.Image.Size==thumbnailSize){
					var users=from user in userList.Cast<User>() select user.Name;
					screenShotListView.UpdateThumbnail(e.UserName,e.Image);
				}else ShowScreenShotWindow(e.UserName,e.Image);
			}else ShowBalloon("画面の取得に失敗しました。",ToolTipIcon.Error);
		}

		private void ShowScreenShotWindow(string name,Bitmap bitmap)
		{
			if(Application.OpenForms.Cast<Form>().Any(f=>f.Name==name))
				(Application.OpenForms[name] as Form2).UpdateScreenShot(bitmap);
			else new Form2(name,bitmap).Show();
		}

		private void contextMenuStrip1_Opening(object sender,CancelEventArgs e)
		{
			toolStripMenuItem1.Enabled=listBox1.SelectedIndex!=-1;
		}

		private void toolStripMenuItem1_Click(object sender,EventArgs e)
		{
			var targets=from user in listBox1.SelectedItems.Cast<User>() select user.Name;
			server.SendScreenShotRequest(0,0,targets.ToArray());
		}

		private void toolStripMenuItem2_Click(object sender,EventArgs e)
		{
			if(screenShotListView==null||screenShotListView.IsDisposed){
				var users=from user in userList.Cast<User>() select user.Name;
				screenShotListView=new Form3(users.ToArray(),thumbnailSize);
				screenShotListView.Show();
			}else screenShotListView.Focus();
		}

		private void Form1_DragEnter(object sender,DragEventArgs e)
		{
			var checkResults=from format in new[]{DataFormats.Text,DataFormats.UnicodeText,DataFormats.OemText,DataFormats.FileDrop}
							 select e.Data.GetDataPresent(format);
			e.Effect=checkResults.Any(result=>result)?DragDropEffects.All:DragDropEffects.None;
		}

		public void RequestScreenShot(int width,int height,string[] users)
		{
			server.SendScreenShotRequest(width,height,users);
		}

		public void SendPowerEvent(PowerEventKind eventKind,string[] machineNames)
		{
			server.SendPowerEvent(eventKind,machineNames);
		}

		private void Form1_DragDrop(object sender,DragEventArgs e)
		{
			if(e.Data.GetDataPresent(typeof(string))){
				textBox1.Text=e.Data.GetData(typeof(string)) as string;
			}else if(e.Data.GetDataPresent(DataFormats.FileDrop)){
				textBox1.Text=(e.Data.GetData(DataFormats.FileDrop) as string[])[0];
			}
			radioButton1.Checked=true;
			button1_Click(null,null);
		}

		private void listBox1_DoubleClick(object sender,EventArgs e)
		{
			if(listBox1.SelectedIndex!=-1){
				var users=from item in listBox1.SelectedItems.Cast<User>() select item.Name;
				server.SendScreenShotRequest(0,0,users.ToArray());
			}
		}

		private void listBox2_DoubleClick(object sender,EventArgs e)
		{
			if(listBox2.SelectedIndex!=-1){
				User user=userList.Cast<User>().FirstOrDefault(u=>u.Name==((Message)listBox2.SelectedItem).Name);
				if(user==null) return;
				if(radioButton3.Checked) textBox1.Text="@"+user.Name+" "+textBox1.Text;
				else listBox1.SelectedItems.Add(user);
			}
		}

		private void AppendLog(string name,string text)
		{
			var color=name==""?Color.Black:userList.Cast<User>().First(user=>user.Name==name).Color;
			listBox2.Items.Insert(0,new Message(name,text,color));
		}

		private void ShowBalloon(string text,ToolTipIcon icon)
		{
			notifyIcon1.ShowBalloonTip(1000,"RemoteController(Server)",text,icon);
		}

		public IPAddress GetLocalIPAddress()
		{
			var unicastAddresses=from i in NetworkInterface.GetAllNetworkInterfaces()
								 where i.GetIPProperties().GatewayAddresses.Count>0
								 select i.GetIPProperties().UnicastAddresses;
			var localAddresses=new List<byte[]>();
			foreach(var ipList in unicastAddresses)
				foreach(var info in ipList)
					localAddresses.Add(info.Address.GetAddressBytes());
			var localAddressBytes=from bytes in localAddresses
								  where
								  bytes[0]==10||
								  (bytes[0]==172&&bytes[1]>=16&&bytes[1]<=31)||
								  (bytes[0]==192&&bytes[1]==168)
								  select bytes;
			return localAddressBytes.Count()!=0?new IPAddress(localAddressBytes.ToList()[0]):null;
		}

		private Color[] GetUserColors()
		{
			var knownColors=from value in Enum.GetValues(typeof(KnownColor)).Cast<KnownColor>()
							select Color.FromKnownColor(value);
			var visibleColors=from color in knownColors
							  let argb=(uint)color.ToArgb()
							  let brightness=color.GetBrightness()
							  where
							  argb!=0xFF000000&&argb!=0xFFFFFFFF&&argb!=0xFF3399FF&&
							  brightness<0.75&&brightness>0.1
							  select color;
			return visibleColors.ToArray();
		}

		private void UpdateTitle()
		{
			Text="RemoteController(Server)@"+localAddress+"("+userList.Count.ToString()+"人接続中)";
		}

		private string[] ParseCommand(string text)
		{
			text=text.Trim();
			int index=0;
			var commanddelim=text[0]=='\"'?'\"':' ';
			for(index++;index<text.Length&&text[index]!=commanddelim;index++) continue;
			index-=(commanddelim==' '?1:0);
			var name=text.Substring(0,index+(index==text.Length?0:1));
			for(index++;index<text.Length&&(text[index]==' '||text[index]=='\t');index++) continue;
			string args="";
			if(index<text.Length){
				var str=text.Substring(index,text.Length-index);
				if(!str.All(c=>c==' '||c=='\t')) args=str;
			}
			return new[]{name,args};
		}

		private void listBox1_DrawItem(object sender,DrawItemEventArgs e)
		{
			if(e.Index!=-1&&e.Index<userList.Count){
				var user=(User)userList[e.Index];
				e.DrawBackground();
				e.Graphics.DrawString(user.Name,e.Font,new SolidBrush(user.Color),e.Bounds);
				e.DrawFocusRectangle();
			}
		}

		private void listBox2_DrawItem(object sender,DrawItemEventArgs e)
		{
			if(e.Index!=-1&&e.Index<userList.Count){
				var message=(Message)listBox2.Items[e.Index];
				e.DrawBackground();
				e.Graphics.DrawString(message.Text,e.Font,new SolidBrush(message.Color),e.Bounds);
				e.DrawFocusRectangle();
			}
		}

		private void textBox1_KeyDown(object sender,KeyEventArgs e)
		{
			if(e.KeyCode==Keys.Up&&currentHistory<maxHistoryCount-1&&currentHistory<commandHistory.Count-1){
				currentHistory++;
				textBox1.Text=commandHistory[currentHistory];
			}else if(e.KeyCode==Keys.Down&&currentHistory>0){
				currentHistory--;
				textBox1.Text=commandHistory[currentHistory];
			}
		}

		private void button2_Click(object sender,EventArgs e)
		{
			if(powerManagerWindow==null) powerManagerWindow=new Form4();
			if(!powerManagerWindow.Visible) powerManagerWindow.Show(this);
		}

		private void ResponseSearchMessage()
		{
			var socket=new UdpClient();
			socket.ExclusiveAddressUse=false;
			socket.Client.Bind(new IPEndPoint(IPAddress.Any,54563));
			for(;;){
				var endPoint=new IPEndPoint(IPAddress.Any,0);
				var message=socket.Receive(ref endPoint);
				if(Encoding.UTF8.GetString(message)=="Hello? Can anyone hear me?"){
					var response=Encoding.UTF8.GetBytes("Yes, I'm hearing your voice. My name is "+localAddress.ToString()+".");
					socket.Send(response,response.Length,endPoint);
				}
			}
		}

		private void button3_Click(object sender,EventArgs e)
		{
			fileTransferWindow.Show();
		}
	}

	class Message
	{
		public string Name{get;protected set;}
		public string Text{get;protected set;}
		public Color Color{get;protected set;}

		public Message()
		{
			Name="";
			Text="";
			Color=Color.Empty;
		}

		public Message(string name,string text,Color color)
		{
			Name=name;
			Text=text;
			Color=color;
		}
	}

	class User
	{
		public string Name{get;protected set;}
		public Color Color{get;protected set;}

		public User()
		{
			Name="";
			Color=Color.Empty;
		}

		public User(string name,Color color)
		{
			Name=name;
			Color=color;
		}
	}
}
