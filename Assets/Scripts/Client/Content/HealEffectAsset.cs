using BigProject.Core.Spells;
using UnityEngine;

namespace BigProject.Client
{
    [CreateAssetMenu(fileName = "Soin", menuName = "Big Project/Sorts/Effets/Soin")]
    public sealed class HealEffectAsset : SpellEffectAsset
    {
        [Min(1)]
        [SerializeField] private int amount = 5;

        [Tooltip("Qui est soigné parmi les unités touchées par la zone du sort.")]
        [SerializeField] private TargetFilter targets = TargetFilter.Self | TargetFilter.Allies;

        public override SpellEffect ToEffect()
        {
            return SpellEffect.Heal(amount, targets);
        }
    }
}
