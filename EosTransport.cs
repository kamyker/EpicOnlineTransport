using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Epic.OnlineServices.P2P;
using Epic.OnlineServices;
using Mirror;
using Epic.OnlineServices.Metrics;
using System.Collections;
using Epic.OnlineServices.Platform;

namespace EpicTransport
{
/// <summary>
/// EOS Transport following the Mirror transport standard
/// </summary>
public class EosTransport : Transport
{
	private const string EPIC_SCHEME = "epic";

	private Client client;
	private Server server;

	private Common activeNode;

	[SerializeField]
	public PacketReliability[] Channels = new PacketReliability[2] {PacketReliability.ReliableOrdered, PacketReliability.UnreliableUnordered};

	[Tooltip("Timeout for connecting in seconds.")]
	public int timeout = 25;

	[Tooltip("The max fragments used in fragmentation before throwing an error.")]
	public int maxFragments = 55;

	public float ignoreCachedMessagesAtStartUpInSeconds = 2.0f;
	private float ignoreCachedMessagesTimer = 0.0f;

	public static RelayControl relayControl = RelayControl.AllowRelays;

	private int packetId = 0;

	public static ProductUserId LocalUserProductId {get; private set;}
	public static string LocalUserProductIdString {get; private set;}
	public static string DisplayName {get; private set;}

	static PlatformInterface platformInterface;
	public static P2PInterface P2PInterface {get; private set;}
	public static MetricsInterface MetricsInterface {get; private set;}

	[SerializeField] bool CollectPlayerMetrics;

	public static void Init(ProductUserId user, PlatformInterface platform, string displayName)
	{
		platformInterface = platform;

		LocalUserProductId = user;
		user.ToString(out var buffer);
		LocalUserProductIdString = buffer;
		P2PInterface = platformInterface.GetP2PInterface();
		MetricsInterface = platformInterface.GetMetricsInterface();
		DisplayName = displayName;
		ChangeRelayStatus();
	}

	private void Awake()
	{
		Debug.Assert(Channels != null && Channels.Length > 0, "No channel configured for EOS Transport.");
		Debug.Assert(Channels.Length < byte.MaxValue, "Too many channels configured for EOS Transport");

		if(Channels[0] != PacketReliability.ReliableOrdered)
		{
			Debug.LogWarning("EOS Transport Channel[0] is not ReliableOrdered, Mirror expects Channel 0 to be ReliableOrdered, only change this if you know what you are doing.");
		}
		if(Channels[1] != PacketReliability.UnreliableUnordered)
		{
			Debug.LogWarning("EOS Transport Channel[1] is not UnreliableUnordered, Mirror expects Channel 1 to be UnreliableUnordered, only change this if you know what you are doing.");
		}
		// StartCoroutine("FetchEpicAccountId");
		// StartCoroutine(nameof(ChangeRelayStatus));
	}

	public override void ClientEarlyUpdate()
	{
		if(activeNode != null)
		{
			ignoreCachedMessagesTimer += Time.deltaTime;

			if(ignoreCachedMessagesTimer <= ignoreCachedMessagesAtStartUpInSeconds)
				activeNode.ignoreAllMessages = true;
			else
			{
				activeNode.ignoreAllMessages = false;

				if(client != null && !client.isConnecting)
				{
					client.Connect(client.hostAddress);
					client.isConnecting = true;
				}
			}
		}

		if(enabled)
		{
			activeNode?.ReceiveData();
		}
	}

	public override void ClientLateUpdate()
	{
	}

	public override void ServerEarlyUpdate()
	{
		if(activeNode != null)
		{
			ignoreCachedMessagesTimer += Time.deltaTime;

			if(ignoreCachedMessagesTimer <= ignoreCachedMessagesAtStartUpInSeconds)
			{
				activeNode.ignoreAllMessages = true;
			}
			else
			{
				activeNode.ignoreAllMessages = false;
			}
		}

		if(enabled)
		{
			activeNode?.ReceiveData();
		}
	}

	public override void ServerLateUpdate()
	{
	}


	public override bool Available()
	{
		return true;
	}

	public override bool ClientConnected() => ClientActive() && client.Connected;

	public override void ClientConnect(string address)
	{
		if(ServerActive())
		{
			Debug.LogError("Transport already running as server!");
			return;
		}

		if(!ClientActive() || client.Error)
		{
			Debug.Log($"Starting client, target address {address}.");

			client = Client.CreateClient(this, address);
			activeNode = client;

			if(CollectPlayerMetrics)
			{
				// Start Metrics collection session
				BeginPlayerSessionOptions sessionOptions = new BeginPlayerSessionOptions();
				// sessionOptions.AccountId = EosTransport.LocalUserProductId;
				sessionOptions.AccountId = new BeginPlayerSessionOptionsAccountId() {External = LocalUserProductIdString};
				sessionOptions.ControllerType = UserControllerType.Unknown;
				sessionOptions.DisplayName = DisplayName;
				sessionOptions.GameSessionId = null;
				sessionOptions.ServerIp = null;
				Result result = MetricsInterface.BeginPlayerSession(sessionOptions);

				if(result == Result.Success)
				{
					Debug.Log("Started Metric Session");
				}
			}
		}
		else
		{
			Debug.LogError("Client already running!");
		}
	}


	public override void ClientConnect(Uri uri)
	{
		if(uri.Scheme != EPIC_SCHEME)
			throw new ArgumentException($"Invalid url {uri}, use {EPIC_SCHEME}://EpicAccountId instead", nameof(uri));

		ClientConnect(uri.Host);
	}

	public override void ClientSend(ArraySegment<byte> segment, int channelId)
	{
		Send(channelId, segment);
	}

	public override void ClientDisconnect()
	{
		if(ClientActive())
		{
			Shutdown();
		}
	}

	public bool ClientActive() => client != null;


	public override bool ServerActive() => server != null;

	public override void ServerStart()
	{
		if(ClientActive())
		{
			Debug.LogError("Transport already running as client!");
			return;
		}

		if(!ServerActive())
		{
			Debug.Log("Starting server.");

			server = Server.CreateServer(this, NetworkManager.singleton.maxConnections);
			activeNode = server;

			if(CollectPlayerMetrics)
			{
				// Start Metrics colletion session
				BeginPlayerSessionOptions sessionOptions = new BeginPlayerSessionOptions();
				// sessionOptions.AccountId = EOSSDKComponent.LocalUserAccountId;
				sessionOptions.AccountId = new BeginPlayerSessionOptionsAccountId() {External = LocalUserProductIdString};
				sessionOptions.ControllerType = UserControllerType.Unknown;
				sessionOptions.DisplayName = DisplayName;
				sessionOptions.GameSessionId = null;
				sessionOptions.ServerIp = null;
				Result result = MetricsInterface.BeginPlayerSession(sessionOptions);

				if(result == Result.Success)
				{
					Debug.Log("Started Metric Session");
				}
			}
		}
		else
		{
			Debug.LogError("Server already started!");
		}
	}

	public override Uri ServerUri()
	{
		UriBuilder epicBuilder = new UriBuilder
		{
			Scheme = EPIC_SCHEME,
			Host = LocalUserProductIdString
		};

		return epicBuilder.Uri;
	}

	public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId)
	{
		if(ServerActive())
		{
			Send(channelId, segment, connectionId);
		}
	}

	public override void ServerDisconnect(int connectionId) => server.Disconnect(connectionId);
	public override string ServerGetClientAddress(int connectionId) => ServerActive() ? server.ServerGetClientAddress(connectionId) : string.Empty;

	private void Send(int channelId, ArraySegment<byte> segment, int connectionId = int.MinValue)
	{
		int packetCount = GetPacketArrayCount(channelId, segment);
		if(packetCount == 1)
		{
			Packet p = new Packet(segment);
			if(connectionId == int.MinValue)
			{
				client.Send(p.ToBytes(), channelId);
			}
			else
			{
				server.SendAll(connectionId, p.ToBytes(), channelId);
			}
			p.Dispose();
			return;
		}
		Packet[] packets = GetPacketArray(channelId, segment, packetCount);

		for(int i = 0; i < packets.Length; i++)
		{
			if(connectionId == int.MinValue)
			{
				client.Send(packets[i].ToBytes(), channelId);
			}
			else
			{
				server.SendAll(connectionId, packets[i].ToBytes(), channelId);
			}
			packets[i].Dispose();
		}

		packetId++;
	}

	private int GetPacketArrayCount(int channelId, ArraySegment<byte> segment)
	{
		return Mathf.CeilToInt((float)segment.Count / (float)GetMaxSinglePacketSize(channelId));
	}

	private Packet[] GetPacketArray(int channelId, ArraySegment<byte> segment, int packetCount)
	{
		Packet[] packets = new Packet[packetCount];
		int maxPacketSize = GetMaxSinglePacketSize(channelId);
		for(int i = 0; i < segment.Count; i += maxPacketSize)
		{
			int fragment = i / maxPacketSize;

			bool more = segment.Count - i > maxPacketSize;
			if(more)
				packets[fragment] = new Packet(new ArraySegment<byte>(segment.Array, segment.Offset + i, maxPacketSize), packetId, fragment, more);
			else
				packets[fragment] = new Packet(new ArraySegment<byte>(segment.Array, segment.Offset + i, segment.Count - i), packetId, fragment, more);

			// packets[fragment].data = new byte[more ? maxPacketSize : segment.Count - i];
			//
			// Array.Copy(segment.Array, i, packets[fragment].data, 0, packets[fragment].data.Length);
		}

		return packets;
	}

	public override void ServerStop()
	{
		if(ServerActive())
		{
			Shutdown();
		}
	}

	public override void Shutdown()
	{
		if(CollectPlayerMetrics)
		{
			// Stop Metrics collection session
			EndPlayerSessionOptions endSessionOptions = new EndPlayerSessionOptions();
			endSessionOptions.AccountId = new EndPlayerSessionOptionsAccountId() {External = LocalUserProductIdString};
			Result result = MetricsInterface.EndPlayerSession(endSessionOptions);

			if(result == Result.Success)
			{
				Debug.LogError("Stopped Metric Session");
			}
		}

		server?.Shutdown();
		client?.Disconnect();

		server = null;
		client = null;
		activeNode = null;
		Debug.Log("Transport shut down.");
	}

	public int GetMaxSinglePacketSize(int channelId) => P2PInterface.MaxPacketSize - 10; // 1159 bytes, we need to remove 10 bytes for the packet header (id (4 bytes) + fragment (4 bytes) + more fragments (1 byte)) 

	public override int GetMaxPacketSize(int channelId) => P2PInterface.MaxPacketSize * maxFragments;

	public override int GetBatchThreshold(int channelId) => P2PInterface.MaxPacketSize; // Use P2PInterface.MaxPacketSize as everything above will get fragmentated and will be counter effective to batching


	private static void ChangeRelayStatus()
	{
		SetRelayControlOptions setRelayControlOptions = new SetRelayControlOptions();
		setRelayControlOptions.RelayControl = relayControl;

		P2PInterface.SetRelayControl(setRelayControlOptions);
	}

	public void ResetIgnoreMessagesAtStartUpTimer()
	{
		ignoreCachedMessagesTimer = 0;
	}

	private void OnDestroy()
	{
		if(activeNode != null)
		{
			Shutdown();
		}
	}
}
}