using System.Collections;
using BigProject.Core.Units;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Représentation visuelle d'une unité. N'est qu'un affichage : aucun état de jeu n'est stocké ici,
    /// et aucun composant réseau n'est nécessaire. La vue est pilotée par les événements de résolution.
    ///
    /// Deux façons de l'obtenir :
    /// - sans prefab : un carré coloré est généré (couleur = équipe) ;
    /// - avec un prefab : le SpriteRenderer du prefab est le corps. Son sprite, sa couleur et son échelle ne
    ///   sont jamais modifiés ; la couleur d'équipe passe sur un marqueur sous le personnage.
    /// Dans les deux cas, l'anneau de sélection et la barre de vie sont ajoutés par le code.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnitView : MonoBehaviour
    {
        private const float BarWidth = 0.8f;
        private const float BarHeight = 0.1f;

        // Ordres d'affichage : au-dessus de la Tilemap et des surbrillances (1 à 9).
        private const int SelectionOrder = 19;
        private const int MarkerOrder = 20;
        private const int BodyOrder = 21;

        private SpriteRenderer body;
        private SpriteRenderer teamMarker;
        private SpriteRenderer selection;
        private Transform barFill;
        private Color bodyColor;
        private Vector3 baseScale = Vector3.one;
        private int maxHealth;
        private UnitAnimation animation;

        /// <summary>Vrai si le prefab a un Animator : les animations remplacent alors les effets de secours (pulsation, rétrécissement).</summary>
        public bool HasAnimation => animation != null;

        public int UnitId { get; private set; }

        /// <summary>PV affichés : suivent l'animation, pas l'état final déjà calculé.</summary>
        public int DisplayedHealth { get; private set; }

        /// <summary>Le corps vient d'un prefab (sprite fourni) plutôt que du carré généré.</summary>
        public bool HasCustomBody { get; private set; }

        public SpriteRenderer BodyRenderer => body;

        public void Init(UnitState unit, Vector3 worldPosition, Color teamColor)
        {
            UnitId = unit.Id;
            maxHealth = unit.MaxHealth;
            baseScale = transform.localScale;
            transform.position = worldPosition;

            // À chercher avant de créer quoi que ce soit : ce que l'on ajoute ensuite a aussi un SpriteRenderer.
            body = GetComponentInChildren<SpriteRenderer>(true);
            HasCustomBody = body != null;
            animation = UnitAnimation.Create(gameObject);

            // Les éléments d'interface vivent dans un objet à l'échelle inverse : leur taille reste celle d'une
            // case, quelle que soit l'échelle donnée au personnage.
            var overlay = new GameObject("Interface").transform;
            overlay.SetParent(transform, false);
            overlay.localScale = new Vector3(Inverse(baseScale.x), Inverse(baseScale.y), 1f);

            selection = SpriteFactory.CreateRenderer("Selection", overlay, new Color(1f, 1f, 1f, 0.9f), SelectionOrder);
            selection.transform.localScale = new Vector3(0.92f, 0.92f, 1f);
            selection.gameObject.SetActive(false);

            float barY = 0.55f;
            if (HasCustomBody)
            {
                body.sortingOrder = Mathf.Max(body.sortingOrder, BodyOrder);
                bodyColor = body.color;
                teamMarker = SpriteFactory.CreateRenderer("Marqueur d'équipe", overlay, MarkerColor(teamColor), MarkerOrder);
                teamMarker.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
                barY = Mathf.Max(barY, body.bounds.max.y - transform.position.y + 0.12f);
            }
            else
            {
                body = SpriteFactory.CreateRenderer("Body", transform, teamColor, BodyOrder);
                body.transform.localScale = new Vector3(0.7f, 0.7f, 1f);
                bodyColor = teamColor;
            }

            SpriteRenderer barBack = SpriteFactory.CreateRenderer("BarBack", overlay, new Color(0f, 0f, 0f, 0.8f), BodyOrder + 1);
            barBack.transform.localPosition = new Vector3(0f, barY, 0f);
            barBack.transform.localScale = new Vector3(BarWidth + 0.04f, BarHeight + 0.04f, 1f);

            var fillRoot = new GameObject("BarFill");
            fillRoot.transform.SetParent(overlay, false);
            fillRoot.transform.localPosition = new Vector3(-BarWidth / 2f, barY, 0f);
            barFill = fillRoot.transform;
            SpriteRenderer fill = SpriteFactory.CreateRenderer("Fill", barFill, new Color(0.3f, 0.9f, 0.35f), BodyOrder + 2);
            fill.transform.localPosition = new Vector3(0.5f, 0f, 0f);

            SetHealth(unit.Health);
            gameObject.SetActive(unit.IsAlive);
        }

        public void SetSelected(bool selected)
        {
            selection.gameObject.SetActive(selected);
        }

        /// <summary>Couleur d'équipe (vert, bleu ou rouge selon le point de vue du joueur).</summary>
        public void SetColor(Color teamColor)
        {
            if (teamMarker != null)
            {
                teamMarker.color = MarkerColor(teamColor);
                return;
            }

            bodyColor = teamColor;
            body.color = teamColor;
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
            transform.localScale = baseScale;
            body.color = bodyColor;
            SetHealth(health);
            gameObject.SetActive(alive);
            if (alive)
            {
                animation?.Reset();
            }
        }

        private void Update()
        {
            animation?.Tick();
        }

        /// <summary>Joue l'animation d'un sort (« Spell1 », « Spell2 »...). Sans effet si le personnage n'a pas d'animation.</summary>
        public void PlayCast(string state)
        {
            animation?.PlayOnce(state);
        }

        public void PlayHit()
        {
            animation?.PlayOnce(UnitAnimation.HitState);
        }

        /// <summary>Coupe l'animation ponctuelle en cours (sort annulé, fin de l'action).</summary>
        public void StopAnimation()
        {
            animation?.Interrupt();
        }

        public IEnumerator MoveTo(Vector3 target, float duration)
        {
            animation?.SetMoving(true);
            Vector3 start = transform.position;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                transform.position = Vector3.Lerp(start, target, elapsed / duration);
                yield return null;
            }

            transform.position = target;
            animation?.SetMoving(false);
        }

        /// <summary>Disparaît sur place, réapparaît à destination.</summary>
        public IEnumerator Teleport(Vector3 target, float duration)
        {
            yield return Scale(0f, duration * 0.5f);
            transform.position = target;
            yield return Scale(1f, duration * 0.5f);
        }

        /// <summary>Apparition d'une invocation : grandit depuis rien.</summary>
        public IEnumerator Appear(float duration)
        {
            gameObject.SetActive(true);
            transform.localScale = Vector3.zero;
            yield return Scale(1f, duration);
        }

        private IEnumerator Scale(float factor, float duration)
        {
            Vector3 start = transform.localScale;
            Vector3 end = baseScale * factor;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                transform.localScale = Vector3.Lerp(start, end, elapsed / duration);
                yield return null;
            }

            transform.localScale = end;
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
                float factor = 1f + 0.25f * Mathf.Sin(elapsed / duration * Mathf.PI);
                transform.localScale = new Vector3(baseScale.x * factor, baseScale.y * factor, baseScale.z);
                yield return null;
            }

            transform.localScale = baseScale;
        }

        public IEnumerator Flash(Color color, float duration)
        {
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                body.color = Color.Lerp(color, bodyColor, elapsed / duration);
                yield return null;
            }

            body.color = bodyColor;
        }

        public IEnumerator Die(float duration)
        {
            // Avec une animation de mort, l'unité reste affichée jusqu'à la fin de la lecture du tour.
            if (animation != null)
            {
                animation.PlayDeath();
                yield break;
            }

            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float remaining = 1f - elapsed / duration;
                transform.localScale = new Vector3(baseScale.x * remaining, baseScale.y * remaining, baseScale.z);
                yield return null;
            }

            gameObject.SetActive(false);
            transform.localScale = baseScale;
        }

        private static Color MarkerColor(Color teamColor)
        {
            return new Color(teamColor.r, teamColor.g, teamColor.b, 0.85f);
        }

        private static float Inverse(float scale)
        {
            return Mathf.Approximately(scale, 0f) ? 1f : 1f / scale;
        }
    }
}
