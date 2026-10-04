using UnityEngine;
using Mirror;

public class NetworkManagerCustom : NetworkManager
{
    public Transform[] playerSpawnPositions;

    private int connectedPlayers = 0;

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        if (connectedPlayers >= 4)
        {
            conn.Disconnect();
            return;
        }
        base.OnServerConnect(conn);
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        Transform startPos = playerSpawnPositions.Length > 0
            ? playerSpawnPositions[connectedPlayers]
            : Instantiate(playerPrefab).transform;

        GameObject player = startPos != null
            ? Instantiate(playerPrefab, startPos.position, startPos.rotation)
            : Instantiate(playerPrefab);

        player.GetComponent<NetworkPlayer>().playerIndex = connectedPlayers;
        NetworkServer.AddPlayerForConnection(conn, player);

        connectedPlayers++;
        Debug.Log($"Игрок {connectedPlayers} подключился");

        var gm = FindObjectOfType<NetworkGameManager>();
        if (gm != null)
            gm.OnPlayerConnected(player.GetComponent<NetworkPlayer>());
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        connectedPlayers = Mathf.Max(0, connectedPlayers - 1);
        base.OnServerDisconnect(conn);
    }
}