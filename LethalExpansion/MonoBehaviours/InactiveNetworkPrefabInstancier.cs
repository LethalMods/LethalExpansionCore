using UnityEngine;
using Unity.Netcode;

namespace LethalExpansionCore.MonoBehaviours;

// Copy of LethalSDK.Component.SI_NetworkPrefabInstancier
// with a single change: "instance.SetActive(true);"
public class LECore_InactiveNetworkPrefabInstancier : MonoBehaviour
{
    public GameObject prefab;

    [HideInInspector]
    public GameObject instance;

    public void Awake()
    {
        if (prefab != null)
        {
            NetworkObject component = prefab.GetComponent<NetworkObject>();
            if (component != null && component.NetworkManager != null && component.NetworkManager.IsHost)
            {
                instance = Object.Instantiate(prefab, base.transform.position, base.transform.rotation, base.transform.parent);
                // It needs to be active before we spawn it otherwise it will not work properly
                instance.SetActive(true);
                instance.GetComponent<NetworkObject>().Spawn();
            }
        }

        base.gameObject.SetActive(value: false);
    }

    public void OnDestroy()
    {
        if (instance != null)
        {
            NetworkObject component = prefab.GetComponent<NetworkObject>();
            if (component != null && component.NetworkManager != null && component.NetworkManager.IsHost)
            {
                instance.GetComponent<NetworkObject>().Despawn();
                Object.Destroy(instance);
            }
        }
    }
}