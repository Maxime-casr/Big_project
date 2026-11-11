using System.Collections.Generic;
using BigProject.Client;
using UnityEditor;
using UnityEngine;

namespace BigProject.EditorTools
{
    /// <summary>
    /// Sous les champs d'un personnage : un résumé de ses sorts (coût, ciblage) et les problèmes éventuels,
    /// pour voir d'un coup d'œil si ses cartes sont jouables avec sa mana.
    /// </summary>
    [CustomEditor(typeof(CharacterAsset))]
    public sealed class CharacterAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var character = (CharacterAsset)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Résumé", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Identifiant", character.Id);

            var seen = new HashSet<SpellAsset>();
            int shown = 0;
            foreach (SpellAsset spell in character.Spells)
            {
                if (spell == null)
                {
                    EditorGUILayout.HelpBox("Une case de la liste des sorts est vide.", MessageType.Warning);
                    continue;
                }

                shown++;
                string line = spell.DisplayName + "  —  " + spell.ManaCost + " mana, " + spell.Targeting;
                EditorGUILayout.LabelField(line);

                if (!seen.Add(spell))
                {
                    EditorGUILayout.HelpBox("Le sort « " + spell.name + " » est présent deux fois.", MessageType.Warning);
                }
                else if (spell.ManaCost > character.MaxMana)
                {
                    EditorGUILayout.HelpBox(
                        "« " + spell.DisplayName + " » coûte " + spell.ManaCost + " : plus que la mana max (" + character.MaxMana + "), il ne sera jamais jouable.",
                        MessageType.Warning);
                }
            }

            if (shown == 0)
            {
                EditorGUILayout.HelpBox("Ce personnage n'a aucun sort : il ne pourra que se déplacer.", MessageType.Info);
            }

            if (character.Playable && character.MovementPoints == 0)
            {
                EditorGUILayout.HelpBox("Un personnage jouable sans PM ne peut pas se déplacer.", MessageType.Info);
            }

            string folder = "Assets/Resources/Characters/";
            if (!AssetDatabase.GetAssetPath(character).StartsWith(folder))
            {
                EditorGUILayout.HelpBox(
                    "Ce personnage n'est pas dans " + folder + " : le jeu ne le chargera pas.", MessageType.Warning);
            }
        }
    }
}
