using System;
using UnityEngine;

namespace CW4Archipelago;

/// <summary>
/// The IL2CPP-injected MonoBehaviour. Kept as a THIN SHIM: no statics, no
/// logic (statics on injected types correlated with EXCEPTION_STACK_OVERFLOW
/// during mission load - see research-findings). All work lives in ModCore.
/// </summary>
public class ModBehaviour : MonoBehaviour
{
    public ModBehaviour(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        ModCore.SafeTick();
    }

    private void LateUpdate()
    {
        ModCore.SafeLateTick();
    }
}
