using System.Collections.Generic;
using BigProject.Core.Spells;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Une unité apparaît sur la case visée, pour l'équipe et le joueur du lanceur, qui la contrôle ensuite
    /// comme ses autres unités. Le sort doit viser une case. Même règle de collision que la téléportation.
    /// </summary>
    [CreateAssetMenu(fileName = "Invocation", menuName = "Big Project/Sorts/Effets/Invocation")]
    public sealed class SummonEffectAsset : SpellEffectAsset
    {
        [SerializeField] private string summonName = "Invocation";
        [Min(1)] [SerializeField] private int maxHealth = 10;
        [Min(0)] [SerializeField] private int maxMana = 4;
        [Min(0)] [SerializeField] private int movementPoints = 2;

        [Tooltip("Les sorts que connaît l'invocation.")]
        [SerializeField] private SpellAsset[] spells;

        public override SpellEffect ToEffect()
        {
            var ids = new List<string>();
            if (spells != null)
            {
                foreach (SpellAsset spell in spells)
                {
                    if (spell != null)
                    {
                        ids.Add(spell.Id);
                    }
                }
            }

            return SpellEffect.SummonUnit(new SummonTemplate(summonName, maxHealth, maxMana, movementPoints, ids));
        }
    }
}
