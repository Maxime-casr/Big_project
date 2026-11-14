using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Pilote l'Animator d'un personnage, uniquement en lui demandant de jouer un état par son nom : aucune
    /// transition ni paramètre à régler dans le controller. Ce qu'il y a dans les états (clips) ne le concerne pas.
    ///
    /// Règles :
    ///  - Idle et Marche tournent en boucle, Marche seulement pendant un déplacement ;
    ///  - une animation ponctuelle (sort, coup reçu) remplace tout de suite celle en cours, et on revient
    ///    à Idle / Marche quand elle se termine ;
    ///  - Idle et Marche ne coupent jamais une animation ponctuelle : le personnage reprend sa boucle ensuite ;
    ///  - Mort ne se coupe pas : une fois mort, plus rien ne change l'animation (jusqu'à <see cref="Reset"/>) ;
    ///  - un état absent du controller est ignoré sans erreur.
    /// </summary>
    public sealed class UnitAnimation
    {
        public const string IdleState = "Idle";
        public const string WalkState = "Marche";
        public const string HitState = "Hit";
        public const string DeathState = "Mort";

        // Sécurité : une animation ponctuelle qui ne se termine jamais (état inexistant, Animator désactivé).
        private const float MaxOneShotSeconds = 5f;

        private readonly Animator animator;
        private string loopState = IdleState;
        private string currentState;
        private string oneShot;
        private float oneShotStart;
        private int oneShotFrame;
        private bool dead;

        private UnitAnimation(Animator animator)
        {
            this.animator = animator;
        }

        /// <summary>Null si l'objet n'a pas d'Animator avec un controller : les unités sans animation fonctionnent comme avant.</summary>
        public static UnitAnimation Create(GameObject root)
        {
            Animator animator = root.GetComponentInChildren<Animator>(true);
            return animator != null && animator.runtimeAnimatorController != null ? new UnitAnimation(animator) : null;
        }

        public bool IsDead => dead;

        /// <summary>Pendant un déplacement : Marche en boucle. Sinon : Idle.</summary>
        public void SetMoving(bool moving)
        {
            loopState = moving ? WalkState : IdleState;
            if (oneShot == null && !dead)
            {
                Start(loopState, false);
            }
        }

        /// <summary>Joue un état une fois (sort, coup reçu) en coupant l'animation en cours, sauf la mort.</summary>
        public void PlayOnce(string state)
        {
            if (dead || string.IsNullOrEmpty(state) || !Has(state))
            {
                return;
            }

            oneShot = state;
            oneShotStart = Time.time;
            oneShotFrame = Time.frameCount;
            Start(state, true);
        }

        /// <summary>Coupe l'animation ponctuelle en cours et reprend Idle / Marche (sort annulé, fin de l'action).</summary>
        public void Interrupt()
        {
            if (dead || oneShot == null)
            {
                return;
            }

            oneShot = null;
            Start(loopState, true);
        }

        public void PlayDeath()
        {
            if (dead)
            {
                return;
            }

            dead = true;
            oneShot = null;
            if (Has(DeathState))
            {
                Start(DeathState, true);
            }
        }

        /// <summary>Retour à zéro : unité vivante qui ne fait rien (début de match, fin de la lecture d'un tour).</summary>
        public void Reset()
        {
            dead = false;
            oneShot = null;
            loopState = IdleState;
            currentState = null;
            Start(IdleState, true);
        }

        /// <summary>À appeler à chaque frame : repasse en Idle / Marche quand l'animation ponctuelle est terminée.</summary>
        public void Tick()
        {
            if (dead || oneShot == null)
            {
                return;
            }

            bool timedOut = Time.time - oneShotStart > MaxOneShotSeconds;
            bool finished = false;

            // Le frame du lancement, l'Animator affiche encore l'ancien état : on attend le suivant.
            if (Time.frameCount > oneShotFrame && animator.isActiveAndEnabled)
            {
                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                finished = info.shortNameHash == Animator.StringToHash(oneShot) &&
                           info.normalizedTime >= 1f && !animator.IsInTransition(0);
            }

            if (finished || timedOut)
            {
                oneShot = null;
                Start(loopState, true);
            }
        }

        private bool Has(string state)
        {
            return animator.isActiveAndEnabled && animator.HasState(0, Animator.StringToHash(state));
        }

        private void Start(string state, bool restart)
        {
            if (!Has(state) || (!restart && currentState == state))
            {
                return;
            }

            currentState = state;
            animator.Play(Animator.StringToHash(state), 0, 0f);
        }
    }
}
