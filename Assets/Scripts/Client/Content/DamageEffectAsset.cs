using BigProject.Core.Spells;
using UnityEngine;

namespace BigProject.Client
{
    [CreateAssetMenu(fileName = "Degats", menuName = "Big Project/Sorts/Effets/Dégâts")]
    public sealed class DamageEffectAsset : SpellEffectAsset
    {
        [Min(1)]
        [SerializeField] private int amount = 5;

        [Tooltip("Qui subit les dégâts parmi les unités touchées par la zone du sort.")]
        [SerializeField] private TargetFilter targets = TargetFilter.Enemies;

        public override SpellEffect ToEffect()
        {
            return SpellEffect.Damage(amount, targets);
        }
    }
}
