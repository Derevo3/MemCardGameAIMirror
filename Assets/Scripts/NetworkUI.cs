using UnityEngine;
using Mirror;
using UnityEngine.UI;

public class NetworkUI : MonoBehaviour
{
    public NetworkManagerCustom networkManager;
    public GameObject hostButton;      // кнопка Host
    public GameObject clientButton;    // кнопка Client (тоже скроем)

    public void StartHost()
    {
        Debug.Log("[HOST] Кнопка нажата");

        if (networkManager == null)
        {
            Debug.LogError("[HOST] NetworkManager не назначен!");
            return;
        }

        networkManager.StartHost();

        if (hostButton != null) hostButton.SetActive(false);
        if (clientButton != null) clientButton.SetActive(false);
    }

    public void StartClient()
    {
        Debug.Log("[CLIENT] Кнопка нажата");

        if (networkManager == null)
        {
            Debug.LogError("[CLIENT] NetworkManager не назначен!");
            return;
        }

        networkManager.StartClient();

        if (hostButton != null) hostButton.SetActive(false);
        if (clientButton != null) clientButton.SetActive(false);
    }
}