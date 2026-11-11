using BigProject.Core.Spells;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Un personnage apparaît sur la case visée, pour l'équipe et le joueur du lanceur, qui le contrôle ensuite
    /// comme ses autres unités. Le sort doit viser une case. Même règle de collision que la téléportation.
    /// Le personnage invoqué (son apparence, ses PV, ses sorts) se règle dans son propre asset.
    /// </summary>
    [CreateAssetMenu(fileName = "Invocation", menuName = "Big Project/Sorts/Effets/Invocation")]
    public sealed class SummonEffectAsset : SpellEffectAsset
    {
        [Tooltip("Le personnage qui apparaît. Un personnage « non jouable » convient très bien (totem, golem...).")]
        [SerializeField] private CharacterAsset character;

        public CharacterAsset Character => character;

        public override SpellEffect ToEffect()
        {
            if (character == null)
            {
                throw new System.InvalidOperationException(
                    "L'effet d'invocation « " + name + " » n'a pas de personnage : glisse-en un dans le champ Character.");
            }

            return SpellEffect.SummonCharacter(character.Id);
        }
    }
}
