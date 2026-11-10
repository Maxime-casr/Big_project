using System.Collections.Generic;
using System.Text;
using BigProject.Core.Spells;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Un sort, créé comme un asset (Create > Big Project > Sorts > Sort). Tout sort placé dans
    /// Assets/Resources/Spells/ est ajouté au jeu automatiquement : aucune liste à tenir à jour.
    /// Le serveur et les clients chargent les mêmes assets ; s'ils diffèrent, la connexion est refusée.
    /// </summary>
    [CreateAssetMenu(fileName = "NouveauSort", menuName = "Big Project/Sorts/Sort")]
    public sealed class SpellAsset : ScriptableObject
    {
        [Header("Identité")]
        [Tooltip("Identifiant unique, sans espaces (ex. fireball). Vide : déduit du nom de l'asset. Ne le change pas en cours de projet.")]
        [SerializeField] private string id;
        [SerializeField] private string displayName = "Nouveau sort";
        [TextArea] [SerializeField] private string description;
        [SerializeField] private Sprite icon;
        [Tooltip("Donné à chaque personnage au début d'un match. Décoche pour un sort réservé à une invocation.")]
        [SerializeField] private bool givenToEveryone = true;

        [Header("Coût")]
        [Min(0)] [SerializeField] private int manaCost = 2;

        [Header("Ciblage")]
        [Tooltip("Case : on vise une case. Unit : on vise une unité, que le sort suit. Self : aucune cible.")]
        [SerializeField] private TargetKind targeting = TargetKind.Cell;
        [Tooltip("Pour le type Unit : qui peut être désigné.")]
        [SerializeField] private TargetFilter unitTargets = TargetFilter.Enemies;
        [Min(0)] [SerializeField] private int minRange;
        [Min(0)] [SerializeField] private int maxRange = 4;
        [SerializeField] private bool requiresLineOfSight = true;
        [Tooltip("Ne peut viser que les cases alignées avec le lanceur.")]
        [SerializeField] private bool straightLineOnly;

        [Header("Zone d'effet")]
        [SerializeField] private AreaShape areaShape = AreaShape.Single;
        [Min(0)] [SerializeField] private int areaRadius;

        [Header("Effets (glisse des assets d'effet ici)")]
        [SerializeField] private SpellEffectAsset[] effects;

        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;

        /// <summary>L'identifiant du sort : celui saisi, sinon le nom de l'asset mis en forme.</summary>
        public string Id => string.IsNullOrWhiteSpace(id) ? Slug(name) : id.Trim();

        public SpellDefinition ToDefinition()
        {
            var list = new List<SpellEffect>();
            if (effects != null)
            {
                foreach (SpellEffectAsset effect in effects)
                {
                    if (effect != null)
                    {
                        list.Add(effect.ToEffect());
                    }
                }
            }

            return new SpellDefinition(
                Id, string.IsNullOrWhiteSpace(displayName) ? Id : displayName,
                manaCost, minRange, maxRange, requiresLineOfSight, straightLineOnly,
                areaShape, areaRadius, list, targeting, unitTargets, givenToEveryone);
        }

        private void OnValidate()
        {
            minRange = Mathf.Max(0, minRange);
            maxRange = Mathf.Max(minRange, maxRange);

            if (effects == null)
            {
                return;
            }

            foreach (SpellEffectAsset effect in effects)
            {
                if ((effect is TeleportEffectAsset || effect is SummonEffectAsset) && targeting != TargetKind.Cell)
                {
                    Debug.LogWarning(
                        "« " + name + " » téléporte ou invoque : son ciblage doit être « Cell ».", this);
                    break;
                }
            }
        }

        private static string Slug(string text)
        {
            var builder = new StringBuilder();
            foreach (char c in text.ToLowerInvariant())
            {
                builder.Append(char.IsLetterOrDigit(c) && c < 128 ? c : '_');
            }

            return builder.ToString().Trim('_');
        }
    }
}
