using System.Collections.Generic;
using BigProject.Core;
using BigProject.Core.Characters;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Un personnage (chevalier, mage, archer...) ou une unité invoquée (totem...), créé comme un asset :
    /// Create > Big Project > Personnages > Personnage. Tout personnage placé dans Assets/Resources/Characters/
    /// est ajouté au jeu automatiquement.
    ///
    /// Il réunit ses caractéristiques, son apparence et la liste de ses sorts : ces sorts sont ses cartes, et
    /// seulement les siennes. Il n'a besoin d'aucun composant réseau : seules les données comptent pour le serveur,
    /// le prefab ne sert qu'à l'affichage chez chaque joueur.
    /// </summary>
    [CreateAssetMenu(fileName = "NouveauPersonnage", menuName = "Big Project/Personnages/Personnage")]
    public sealed class CharacterAsset : ScriptableObject
    {
        [Header("Identité")]
        [Tooltip("Identifiant unique, sans espace ni accent (ex. knight). Vide : déduit du nom de l'asset. Ne le change pas en cours de projet.")]
        [SerializeField] private string id;
        [SerializeField] private string displayName = "Nouveau personnage";
        [TextArea] [SerializeField] private string description;
        [SerializeField] private Sprite icon;

        [Tooltip("Visible dans le choix du personnage. Décoche pour une unité qui n'existe que par invocation (totem, golem...).")]
        [SerializeField] private bool playable = true;

        [Header("Apparence")]
        [Tooltip("Le prefab affiché sur le plateau : un SpriteRenderer suffit. Vide : le prefab par défaut du combat, sinon un carré.")]
        [SerializeField] private GameObject prefab;

        [Header("Caractéristiques")]
        [Min(1)] [SerializeField] private int maxHealth = 30;
        [Min(0)] [SerializeField] private int maxMana = 8;
        [Tooltip("Cases parcourues par tour.")]
        [Min(0)] [SerializeField] private int movementPoints = 4;

        [Header("Sorts : les cartes de ce personnage, dans l'ordre")]
        [SerializeField] private SpellAsset[] spells;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public GameObject Prefab => prefab;
        public bool Playable => playable;
        public int MaxHealth => maxHealth;
        public int MaxMana => maxMana;
        public int MovementPoints => movementPoints;
        public IReadOnlyList<SpellAsset> Spells => spells ?? new SpellAsset[0];

        /// <summary>L'identifiant du personnage : celui saisi, sinon le nom de l'asset mis en forme.</summary>
        public string Id => string.IsNullOrWhiteSpace(id) ? AssetIds.Slug(name) : id.Trim();

        public CharacterDefinition ToDefinition()
        {
            var spellIds = new List<string>();
            foreach (SpellAsset spell in Spells)
            {
                if (spell != null && !spellIds.Contains(spell.Id))
                {
                    spellIds.Add(spell.Id);
                }
            }

            return new CharacterDefinition(Id, DisplayName, maxHealth, maxMana, movementPoints, spellIds, playable);
        }

        private void OnValidate()
        {
            if (!IdRules.IsValid(Id))
            {
                Debug.LogWarning("Identifiant « " + Id + " » invalide : " + IdRules.Description, this);
            }

            if (spells == null)
            {
                return;
            }

            var seen = new HashSet<SpellAsset>();
            foreach (SpellAsset spell in spells)
            {
                if (spell != null && !seen.Add(spell))
                {
                    Debug.LogWarning("« " + name + " » a deux fois le sort « " + spell.name + " ».", this);
                    break;
                }
            }
        }
    }
}
