using System;
using System.Collections.Generic;

namespace TiltBox.Core
{
    public enum SessionStatus
    {
        Playing = 0,
        Won = 1,
        OutOfMoves = 2,
    }

    /// <summary>
    /// One attempt at a level: current state, swipe count, undo history and win / out-of-moves rules (GDD §2, §6).
    /// Knows nothing about tokens, coins or ads; the meta layer (week 4) decides whether an undo is allowed.
    /// </summary>
    public sealed class LevelSession
    {
        private readonly Stack<BoardState> _history = new Stack<BoardState>();
        private int _bonusMoves;

        public LevelDefinition Level { get; }
        public BoardState State { get; private set; }
        public int MovesUsed { get; private set; }
        public SessionStatus Status { get; private set; }

        public LevelSession(LevelDefinition level)
        {
            Level = level ?? throw new ArgumentNullException(nameof(level));
            Restart();
        }

        /// <summary>Swipes allowed. Daily puzzles have no limit (int.MaxValue).</summary>
        public int Limit => Level.HasMoveLimit ? Level.Limit + _bonusMoves : int.MaxValue;
        public int MovesLeft => Math.Max(0, Limit - MovesUsed);

        /// <summary>Swipes left that still earn 3 stars (shown on the HUD). 0 once past par.</summary>
        public int MovesLeftForThreeStars => Math.Max(0, Level.Par - MovesUsed);

        public bool CanUndo => _history.Count > 0 && Status != SessionStatus.Won;

        /// <summary>
        /// Applies a player swipe (tower rules may reverse or forbid it). Returns null when the session is not
        /// accepting input (won or out of moves). An ignored or forbidden swipe is returned with
        /// Changed == false and costs no move.
        /// </summary>
        public SwipeResult Swipe(Direction swipe)
        {
            if (Status != SessionStatus.Playing) return null;
            RuleSet rules = Level.Rules;
            Direction dir = rules.BoardDirection(swipe);
            if (!rules.AllowsMove(dir)) return new SwipeResult(dir, State, State, System.Array.Empty<BlockMove>());
            SwipeResult result = SwipeResolver.Resolve(Level.Layout, State, dir, rules.ConveyorPasses);
            if (!result.Changed) return result;

            _history.Push(State);
            State = result.After;
            MovesUsed++;
            UpdateStatus();
            return result;
        }

        /// <summary>Steps back one swipe and refunds it. Also leaves the out-of-moves state.</summary>
        public bool Undo()
        {
            if (!CanUndo) return false;
            State = _history.Pop();
            MovesUsed--;
            UpdateStatus();
            return true;
        }

        public void Restart()
        {
            _history.Clear();
            _bonusMoves = 0;
            State = Level.Start;
            MovesUsed = 0;
            Status = SessionStatus.Playing;
        }

        /// <summary>Extra swipes, e.g. the "+5 moves" rewarded ad (§10). Resumes play if out of moves.</summary>
        public void AddMoves(int count)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            _bonusMoves += count;
            UpdateStatus();
        }

        /// <summary>3 = par or better, 2 = par + 2 or better, 1 = any win, 0 = not won (§6).</summary>
        public int Stars => Status == SessionStatus.Won ? StarsFor(MovesUsed, Level.Par) : 0;

        public static int StarsFor(int movesUsed, int par)
        {
            if (movesUsed <= par) return 3;
            if (movesUsed <= par + 2) return 2;
            return 1;
        }

        private void UpdateStatus()
        {
            if (State.IsSolved) Status = SessionStatus.Won;
            else if (MovesUsed >= Limit) Status = SessionStatus.OutOfMoves;
            else Status = SessionStatus.Playing;
        }
    }
}
