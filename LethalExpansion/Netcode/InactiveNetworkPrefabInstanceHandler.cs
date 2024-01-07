using UnityEngine;
using Unity.Netcode;

namespace LethalExpansionCore.Netcode;

public class InactiveNetworkPrefabInstanceHandler : INetworkPrefabInstanceHandler
{
	private readonly GameObject Prefab;

	public InactiveNetworkPrefabInstanceHandler(GameObject prefab)
	{
		Prefab = prefab;
	}

	public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
	{
		UnityEngine.GameObject instance = UnityEngine.GameObject.Instantiate(Prefab, position, rotation);
		instance.SetActive(true);
		return instance.GetComponent<NetworkObject>();
	}

	public void Destroy(NetworkObject networkObject)
	{
		UnityEngine.GameObject.Destroy(networkObject.gameObject);
	}
}