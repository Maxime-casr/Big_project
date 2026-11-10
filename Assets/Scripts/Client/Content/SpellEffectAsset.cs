using BigProject.Core.Spells;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Un effet de sort, créé comme un asset (Create > Big Project > Sorts > Effets) puis glissé dans la liste
    /// d'effets d'un ou plusieurs sorts. Un même effet peut donc servir à plusieurs sorts.
    /// </summary>
    public abstract class SpellEffectAsset : ScriptableObject
    {
        /// <summary>L'effet tel que le Core le connaît : c'est lui que le serveur et les clients appliquent.</summary>
        public abstract SpellEffect ToEffect();
    }
}
