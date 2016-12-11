using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace Server
{
	public partial class Form4 : Form
	{
		Dictionary<string,MachineInfo> machines=new Dictionary<string,MachineInfo>();
		IPAddress localAddress,classABroadcastAddress;

		public Form4()
		{
			InitializeComponent();
			localAddress=((Form1)Application.OpenForms.Cast<Form>().First(f=>f is Form1)).GetLocalIPAddress();
			classABroadcastAddress=IPAddress.Parse("10.255.255.255");
		}

		private void UpdatePowerState()
		{
			var tasks=machines.Select(machine=>Task.Factory.StartNew(()=>
			{
				using(var ping=new Ping()){
					var reply=ping.Send(machine.Value.IPAddress,1000);
					bool succeeded=reply.Status==IPStatus.Success;
					machine.Value.PowerState=succeeded?PowerState.On:PowerState.Off;
					Invoke((Action<bool>)(result=>listView1.Items[machine.Key].SubItems[1].Text=result?"オン":"オフ"),succeeded);
				}
			},TaskCreationOptions.LongRunning));
			Task.WaitAll(tasks.ToArray());
			Invoke((Action)(()=>toolStripButton2.Enabled=true));
		}

		private byte[] CreateMagicPacket(string machineName)
		{
			var header=Enumerable.Repeat((byte)0xFF,6);
			var body=Enumerable.Repeat(machines[machineName].PhysicalAddress,16).SelectMany(x=>x);
			return header.Concat(body).ToArray();
		}

		private void Form4_Load(object sender,EventArgs e)
		{
			var json=new Json.Json();
			var machinesDir=new DirectoryInfo(Environment.CurrentDirectory+"\\Machines");
			if(!machinesDir.Exists) machinesDir.Create();
			foreach(var file in machinesDir.GetFiles()){
				using(var stream=file.OpenText()){
					object obj=null;
					try{
						obj=json.Parse(stream.ReadToEnd());
					}catch(Exception){}
					if(obj is Dictionary<string,object>){
						var data=(Dictionary<string,object>)obj;
						if(data.Get("mac") is string&&data.Get("ip") is string){
							var mac=PhysicalAddress.Parse((string)data["mac"]).GetAddressBytes();
							var ip=IPAddress.Parse((string)data["ip"]);
							machines.Add(file.Name,new MachineInfo(mac,ip));
							var item=new ListViewItem(new string[]{file.Name,"",ip.ToString(),(string)data["mac"]}){Name=file.Name};
							listView1.Items.Add(item);
						}
					}
				}
			}
			toolStripButton2_Click(null,null);
		}

		private void toolStripButton1_Click(object sender,EventArgs e)
		{
			toolStripButton1.Enabled=false;
			var socket=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);
			socket.SetSocketOption(SocketOptionLevel.Socket,SocketOptionName.Broadcast,true);
			foreach(var item in listView1.SelectedItems.Cast<ListViewItem>()){
				var buffer=CreateMagicPacket(item.Name);
				socket.SendTo(buffer,new IPEndPoint(GetBroadcastAddress(machines[item.Name].IPAddress),7));
			}
			toolStripButton1.Enabled=true;
		}

		private void RaisePowerEvent(PowerEventKind eventKind)
		{
			var machineNames=listView1.SelectedItems.Cast<ListViewItem>().Select(i=>i.Name);
			if(this.Owner is Form1)	(this.Owner as Form1).SendPowerEvent(eventKind,machineNames.ToArray());
		}

		private void toolStripButton2_Click(object sender,EventArgs e)
		{
			toolStripButton2.Enabled=false;
			ThreadPool.QueueUserWorkItem(_=>UpdatePowerState());
		}

		private void Form4_FormClosing(object sender,FormClosingEventArgs e)
		{
			switch(e.CloseReason){
			case CloseReason.UserClosing:
				this.Visible=false;
				e.Cancel=true;
				break;
			case CloseReason.None:
			case CloseReason.TaskManagerClosing:
				e.Cancel=true;
				break;
			}
		}

		private IPAddress GetBroadcastAddress(IPAddress localAddress)
		{
			var addressBytes=localAddress.GetAddressBytes();
			if(addressBytes[0]==10) return classABroadcastAddress;
			else if(addressBytes[0]==172&&addressBytes[1]>=16&&addressBytes[1]<=31){
				addressBytes[2]=255;
				addressBytes[3]=255;
				return new IPAddress(addressBytes);
			}else if(addressBytes[0]==192&&addressBytes[1]==168){
				addressBytes[3]=255;
				return new IPAddress(addressBytes);
			}else return IPAddress.Broadcast;
		}
	}

	enum PowerState
	{
		On,Off
	}

	class MachineInfo
	{
		public byte[] PhysicalAddress{get;set;}
		public IPAddress IPAddress{get;set;}
		public PowerState PowerState{get;set;}

		public MachineInfo(byte[] physicalAddress,IPAddress ipAddress)
		{
			PhysicalAddress=physicalAddress;
			IPAddress=ipAddress;
			PowerState=PowerState.Off;
		}
	}
}
