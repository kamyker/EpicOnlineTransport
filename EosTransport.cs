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

	public float ignoreCachedMessagesAtStartUpInSeconds = 2.0f;
	private float ignoreCachedMessagesTimer = 0.0f;

	public static RelayControl relayControl = RelayControl.AllowRelays;

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

		// StartCoroutine("FetchEpicAccountId");
		// StartCoroutine(nameof(ChangeRelayStatus));
	}

	private void LateUpdate()
	{
		if(activeNode != null)
		{
			ignoreCachedMessagesTimer += Time.deltaTime;

			if(ignoreCachedMessagesTimer <= ignoreCachedMessagesAtStartUpInSeconds)
				activeNode.ignoreAllMessages = true;
			else
			{
				activeNode.ignoreAllMessages = false;

				if(client is {isConnecting: false})
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

	public override bool Available()
	{
		return true;
	}

	public override bool ClientConnected() => ClientActive() && client.Connected;

	public override void ClientConnect(string address)
	{
		StartCoroutine("FetchEpicAccountId");

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

	public override void ClientSend(int channelId, ArraySegment<byte> segment)
	{
		byte[] data = new byte[segment.Count];
		Array.Copy(segment.Array, segment.Offset, data, 0, segment.Count);
		client.Send(data, channelId);
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

	public override void ServerSend(int connectionId, int channelId, ArraySegment<byte> segment)
	{
		if(ServerActive())
		{
			byte[] data = new byte[segment.Count];
			Array.Copy(segment.Array, segment.Offset, data, 0, segment.Count);
			server.SendAll(connectionId, data, channelId);
		}
	}

	public override bool ServerDisconnect(int connectionId) => ServerActive() && server.Disconnect(connectionId);
	public override string ServerGetClientAddress(int connectionId) => ServerActive() ? server.ServerGetClientAddress(connectionId) : string.Empty;

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

	public override int GetMaxPacketSize(int channelId)
	{
		return P2PInterface.MaxPacketSize;
	}
	

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