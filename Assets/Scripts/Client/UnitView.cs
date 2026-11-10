using System;
using System.Collections;
using BigProject.Core.Units;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Représentation visuelle d'une unité. N'est qu'un affichage : aucun état de jeu n'est stocké ici,
    /// et aucun composant réseau n'est nécessaire. La vue est pilotée par les événements de résolution.
    /// </summary>
    public sealed class UnitView : MonoBehaviour
    {
        private const float BarWidth = 0.8f;
        private const float BarHeight = 0.1f;

        private SpriteRenderer body;
        private SpriteRenderer selection;
        private Transform barFill;
        private Color baseColor;
        private int maxHealth;

        public int UnitId { get; private set; }
        /// <summary>PV affichés : suivent l'animation, pas l'état final déjà calculé.</summary>
        public int DisplayedHealth { get; private set; }

        public void Init(UnitState unit, Vector3 worldPosition, Color color)
        {
            UnitId = unit.Id;
            maxHealth = unit.MaxHealth;
            baseColor = color;
            transform.position = worldPosition;

            selection = SpriteFactory.CreateRenderer("Selection", transform, new Color(1f, 1f, 1f, 0.9f), 20);
            selection.transform.localScale = new Vector3(0.92f, 0.92f, 1f);
            selection.gameObject.SetActive(false);

            body = SpriteFactory.CreateRenderer("Body", transform, color, 21);
            body.transform.localScale = new Vector3(0.7f, 0.7f, 1f);

            SpriteRenderer barBack = SpriteFactory.CreateRenderer("BarBack", transform, new Color(0f, 0f, 0f, 0.8f), 22);
            barBack.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            barBack.transform.localScale = new Vector3(BarWidth + 0.04f, BarHeight + 0.04f, 1f);

            var fillRoot = new GameObject("BarFill");
            fillRoot.transform.SetParent(transform, false);
            fillRoot.transform.localPosition = new Vector3(-BarWidth / 2f, 0.55f, 0f);
            barFill = fillRoot.transform;
            SpriteRenderer fill = SpriteFactory.CreateRenderer("Fill", barFill, new Color(0.3f, 0.9f, 0.35f), 23);
            fill.transform.localPosition = new Vector3(0.5f, 0f, 0f);

            SetHealth(unit.Health);
            gameObject.SetActive(unit.IsAlive);
        }

        public void SetSelected(bool selected)
        {
            selection.gameObject.SetActive(selected);
        }

        public void SetColor(Color color)
        {
            baseColor = color;
            body.color = color;
        }

        public void SetHealth(int health)
        {
            DisplayedHealth = health;
            float ratio = maxHealth <= 0 ? 0f : Mathf.Clamp01(health / (float)maxHealth);
            barFill.localScale = new Vector3(BarWidth * ratio, BarHeight, 1f);
        }

        public void Snap(Vector3 worldPosition, int health, bool alive)
        {
            transform.position = worldPosition;
            transform.localScale = Vector3.one;
            body.color = baseColor;
            SetHealth(health);
            gameObject.SetActive(alive);
        }

        public IEnumerator MoveTo(Vector3 target, float duration)
        {
            Vector3 start = transform.position;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                transform.position = Vector3.Lerp(start, target, elapsed / duration);
                yield return null;
            }

            transform.position = target;
        }

        /// <summary>Petit mouvement vers une case sans y entrer (déplacement bloqué).</summary>
        public IEnumerator Bump(Vector3 toward, float duration)
        {
            Vector3 start = transform.position;
            Vector3 peak = start + (toward - start) * 0.25f;
            yield return MoveTo(peak, duration * 0.5f);
            yield return MoveTo(start, duration * 0.5f);
        }

        public IEnumerator Pulse(float duration)
        {
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float scale = 1f + 0.25f * Mathf.Sin(elapsed / duration * Mathf.PI);
                transform.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }

            transform.localScale = Vector3.one;
        }

        public IEnumerator Flash(Color color, float duration)
        {
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                body.color = Color.Lerp(color, baseColor, elapsed / duration);
                yield return null;
            }

            body.color = baseColor;
        }

        public IEnumerator Die(float duration)
        {
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float t = elapsed / duration;
                transform.localScale = new Vector3(1f - t, 1f - t, 1f);
                yield return null;
            }

            gameObject.SetActive(false);
            transform.localScale = Vector3.one;
        }
    }
}
