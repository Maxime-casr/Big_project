using BigProject.Core.Spells;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Le lanceur apparaît sur la case visée. Le sort doit viser une case. Si la case est prise au même
    /// instant, l'initiative est tirée au sort ; si elle est déjà occupée, le sort échoue.
    /// </summary>
    [CreateAssetMenu(fileName = "Teleportation", menuName = "Big Project/Sorts/Effets/Téléportation")]
    public sealed class TeleportEffectAsset : SpellEffectAsset
    {
        public override SpellEffect ToEffect()
        {
            return SpellEffect.Teleport();
        }
    }
}
