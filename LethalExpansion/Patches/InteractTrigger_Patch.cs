using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalExpansionCore.Patches;

[HarmonyPatch(typeof(InteractTrigger))]
internal class InteractTrigger_Patch
{
    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    public static void Start_Postfix(AudioReverbTrigger __instance)
    {
        __instance.gameObject.AddComponent<InteractTrigger_Extension>();
    }
}

public class InteractTrigger_Extension : MonoBehaviour
{
    private InteractTrigger trigger;
    
    private void Awake()
    {
        trigger = this.GetComponent<InteractTrigger>();
    }

    private void OnTriggerExit(Collider other)
    {
        if (trigger == null)
        {
            return;
        }

        if (!trigger.touchTrigger)
        {
            return;
        }

        PlayerControllerB player = other.gameObject.GetComponent<PlayerControllerB>();
        if (other.gameObject.CompareTag("Player") && player != null && player.IsOwner)
        {
            trigger.onStopInteract.Invoke(player);
        }
    }
}
