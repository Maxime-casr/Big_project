using System;
using System.Collections.Generic;
using BigProject.Core.Match;
using BigProject.Core.Orders;
using BigProject.Core.Resolution;
using BigProject.Core.Session;
using BigProject.Core.Spells;
using BigProject.Core.Units;

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
        private float planningSeconds;
        private bool hasConnected;
        private readonly Dictionary<int, IReadOnlyList<UnitOrder>> allyDrafts = new Dictionary<int, IReadOnlyList<UnitOrder>>();

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
        public int PlansReady { get; private set; }
        public int PlansExpected { get; private set; }

        /// <summary>
        /// Le chrono ne descend pas pendant que le client rejoue le tour précédent : il ne s'affiche
        /// qu'à partir de la durée de planification annoncée (même règle que le serveur).
        /// </summary>
        public float PlanningSecondsRemaining =>
            planningSeconds > 0f ? Math.Max(0f, Math.Min(planningSeconds, planningRemaining)) : 0f;

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
            if (State != null && State.Phase == MatchPhase.Planning && planningSeconds > 0f && planningRemaining > 0f)
            {
                planningRemaining = Math.Max(0f, planningRemaining - deltaSeconds);
            }
        }

        public void SendDraft(IReadOnlyList<UnitPlan> units)
        {
            if (State != null && State.Phase == MatchPhase.Planning)
            {
                transport.Send(MessageCodec.Encode(new DraftMessage(units)));
            }
        }

        public bool TryGetAllyDraft(int unitId, out IReadOnlyList<UnitOrder> orders)
        {
            return allyDrafts.TryGetValue(unitId, out orders);
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
                    StartPlanningTimer(started.PlanningSeconds, PlaybackTiming.InitialAllowanceSeconds);
                    MatchStarted?.Invoke();
                    break;

                case RoundResolvedMessage resolved:
                    if (State == null)
                    {
                        break;
                    }

                    resolved.Snapshot.ApplyTo(State);
                    StartPlanningTimer(resolved.PlanningSeconds, PlaybackTiming.Estimate(resolved.Result));
                    RoundResolved?.Invoke(resolved.Result);
                    break;

                case PlanStatusMessage status:
                    PlansReady = status.Ready;
                    PlansExpected = status.Expected;
                    break;

                case AllyDraftMessage ally:
                    if (State == null || State.Phase != MatchPhase.Planning)
                    {
                        break;
                    }

                    foreach (UnitPlan unitPlan in ally.Units)
                    {
                        UnitState unit = State.GetUnit(unitPlan.UnitId);
                        if (unit != null && unit.PlayerId != LocalPlayerId)
                        {
                            allyDrafts[unitPlan.UnitId] = unitPlan.Orders;
                        }
                    }

                    break;

                case PlanRejectedMessage rejected:
                    PlanRejected?.Invoke(rejected.Reason);
                    break;

                case ServerErrorMessage error:
                    Failed?.Invoke(error.Text);
                    break;
            }
        }

        private void StartPlanningTimer(float seconds, float playbackAllowance)
        {
            planningSeconds = seconds;
            planningRemaining = seconds > 0f ? seconds + playbackAllowance : 0f;
            PlansReady = 0;
            PlansExpected = State.GetActivePlayerIds().Count;
            allyDrafts.Clear();
        }
    }
}
