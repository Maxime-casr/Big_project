using System;
using BigProject.Core.Match;
using BigProject.Core.Orders;
using BigProject.Core.Resolution;
using BigProject.Core.Session;
using BigProject.Core.Spells;

namespace BigProject.Core.Net
{
    /// <summary>
    /// Session de jeu en ligne côté client. Ne calcule rien : envoie les plans au serveur et applique
    /// ce qu'il renvoie (état complet + événements à rejouer). Appeler Tick à chaque frame.
    /// </summary>
    public sealed class NetworkMatchSession : IMatchSession
    {
        private readonly IClientTransport transport;
        private readonly string playerName;
        private float planningRemaining;
        private bool hasConnected;

        public NetworkMatchSession(IClientTransport transport, SpellCatalog spells, string playerName)
        {
            this.transport = transport;
            this.playerName = playerName;
            Spells = spells;

            transport.Connected += OnConnected;
            transport.Disconnected += OnDisconnected;
            transport.MessageReceived += OnMessage;
        }

        public MatchState State { get; private set; }
        public SpellCatalog Spells { get; }
        public int LocalPlayerId { get; private set; } = -1;
        public bool IsInMatch => State != null;
        public float PlanningSecondsRemaining => planningRemaining;

        public event Action<RoundResult> RoundResolved;
        public event Action<string> PlanRejected;
        /// <summary>Le serveur a créé le match : State et LocalPlayerId sont disponibles.</summary>
        public event Action MatchStarted;
        public event Action<int, int> QueueChanged;
        /// <summary>Erreur ou perte de connexion (message affichable).</summary>
        public event Action<string> Failed;

        public bool TrySubmitPlan(TurnPlan plan, out string error)
        {
            if (State == null || State.Phase != MatchPhase.Planning)
            {
                error = "Le match n'accepte pas de plan pour le moment.";
                return false;
            }

            transport.Send(MessageCodec.Encode(new SubmitPlanMessage(plan.Units)));
            error = null;
            return true;
        }

        public void Tick(float deltaSeconds)
        {
            if (State != null && State.Phase == MatchPhase.Planning && planningRemaining > 0f)
            {
                planningRemaining = Math.Max(0f, planningRemaining - deltaSeconds);
            }
        }

        private void OnConnected()
        {
            hasConnected = true;
            transport.Send(MessageCodec.Encode(new HelloMessage(NetProtocol.Version, playerName)));
        }

        private void OnDisconnected()
        {
            Failed?.Invoke(hasConnected ? "Connexion au serveur perdue." : "Impossible de joindre le serveur.");
        }

        private void OnMessage(byte[] data)
        {
            try
            {
                Handle(MessageCodec.Decode(data));
            }
            catch (ProtocolException exception)
            {
                Failed?.Invoke("Réponse invalide du serveur : " + exception.Message);
            }
        }

        private void Handle(NetMessage message)
        {
            switch (message)
            {
                case QueueStatusMessage queue:
                    QueueChanged?.Invoke(queue.Waiting, queue.Needed);
                    break;

                case MatchStartedMessage started:
                    State = started.Snapshot.CreateState();
                    LocalPlayerId = started.LocalPlayerId;
                    StartPlanningTimer(started.PlanningSeconds);
                    MatchStarted?.Invoke();
                    break;

                case RoundResolvedMessage resolved:
                    if (State == null)
                    {
                        break;
                    }

                    resolved.Snapshot.ApplyTo(State);
                    StartPlanningTimer(resolved.PlanningSeconds);
                    RoundResolved?.Invoke(resolved.Result);
                    break;

                case PlanRejectedMessage rejected:
                    PlanRejected?.Invoke(rejected.Reason);
                    break;

                case ServerErrorMessage error:
                    Failed?.Invoke(error.Text);
                    break;
            }
        }

        private void StartPlanningTimer(float seconds)
        {
            planningRemaining = seconds;
        }
    }
}
