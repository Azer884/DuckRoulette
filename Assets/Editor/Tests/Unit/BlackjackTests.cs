using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace DuckRoulette.Tests.Unit
{
    // CardDeck is server-authoritative; its private server helpers are pure dictionary/list logic
    // once the RPC broadcasts (which no-op with a logged error offline) are accounted for.
    [TestFixture, Category("Unit")]
    public class CardDeckTests
    {
        private const string TablePrefabPath = "Assets/Prefabs/Map/BlackjackTable.prefab";

        private GameObject _go;
        private CardDeck _deck;
        private CardDeck _previousInstance;

        internal static List<Card> LoadAuthoredDeck()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TablePrefabPath);
            Assert.That(prefab, Is.Not.Null, $"Missing {TablePrefabPath}");
            var authored = prefab.GetComponentInChildren<CardDeck>(true);
            Assert.That(authored, Is.Not.Null, "BlackjackTable prefab has no CardDeck");
            return new List<Card>(authored.cardDeck);
        }

        [SetUp]
        public void SetUp()
        {
            Assume.That(NetworkManager.Singleton, Is.Null);
            _previousInstance = CardDeck.instance;
            _go = new GameObject("CardDeck (unit test)") { hideFlags = HideFlags.DontSave };
            _deck = _go.AddComponent<CardDeck>();
            _deck.cardDeck = LoadAuthoredDeck();
            Reflect.Call(_deck, "PopulateDictionary");
        }

        [TearDown]
        public void TearDown()
        {
            CardDeck.instance = _previousInstance;
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }
        }

        private List<ulong> SeatOrder => Reflect.Get<List<ulong>>(_deck, "_seatOrder");

        private void Seat(ulong clientId, bool done, int sum)
        {
            SeatOrder.Add(clientId);
            _deck.playerInGameList[clientId] = 0;
            _deck.playerInCurrentGameList[clientId] = (done, sum);
        }

        private void StockOnly(int cardValue, int count = 4)
        {
            foreach (Card card in _deck.cardDictionary.Keys.ToList())
            {
                _deck.cardDictionary[card] = card.cardValue == cardValue ? count : 0;
            }
        }

        private void DealCard(ulong clientId, bool isFirstCard) => Reflect.Call(_deck, "DealCard", clientId, isFirstCard);

        [Test]
        public void AuthoredDeck_HasOneDistinctCardPerValueOneToTen()
        {
            Assert.That(_deck.cardDeck, Has.None.Null);
            Assert.That(_deck.cardDeck.Distinct().Count(), Is.EqualTo(_deck.cardDeck.Count), "duplicate Card asset in the deck");
            Assert.That(_deck.cardDeck.Select(c => c.cardValue).OrderBy(v => v), Is.EqualTo(Enumerable.Range(1, 10)));
        }

        [Test]
        public void AuthoredDeck_EveryCardHasArtwork()
        {
            foreach (Card card in _deck.cardDeck)
            {
                Assert.That(card.artworks, Is.Not.Null.And.Not.Empty, $"card {card.name} has no artwork meshes");
                Assert.That(card.artworks, Has.None.Null, $"card {card.name} has a missing artwork mesh");
            }
        }

        [Test]
        public void PopulateDictionary_FourOfEachValue_FortyCardsTotal220()
        {
            Assert.That(_deck.cardDictionary.Count, Is.EqualTo(10));
            Assert.That(_deck.cardDictionary.Values, Has.All.EqualTo(4));
            Assert.That(_deck.cardDictionary.Sum(kv => kv.Value), Is.EqualTo(40));
            Assert.That(_deck.cardDictionary.Sum(kv => kv.Key.cardValue * kv.Value), Is.EqualTo(220));
        }

        [Test]
        public void PopulateDictionary_DuplicateAndNullEntries_DoNotThrow()
        {
            Card first = _deck.cardDeck[0];
            _deck.cardDeck.Add(first);
            _deck.cardDeck.Add(null);

            Assert.DoesNotThrow(() => Reflect.Call(_deck, "PopulateDictionary"));
            Assert.That(_deck.cardDictionary.Count, Is.EqualTo(10));
            Assert.That(_deck.cardDictionary[first], Is.EqualTo(4));
        }

        [Test]
        public void GetRandomCard_DrawsWholeShoe_ThenReturnsNull()
        {
            var drawnPerValue = new Dictionary<int, int>();
            for (int i = 0; i < 40; i++)
            {
                Card card = _deck.GetRandomCard();
                Assert.That(card, Is.Not.Null, $"shoe ran dry after {i} cards");
                Assert.That(_deck.cardDictionary[card], Is.GreaterThan(0), "drew a card with no stock left");
                _deck.cardDictionary[card]--;
                drawnPerValue[card.cardValue] = drawnPerValue.TryGetValue(card.cardValue, out int n) ? n + 1 : 1;
            }

            Assert.That(drawnPerValue.Values, Has.All.EqualTo(4));
            Assert.That(_deck.GetRandomCard(), Is.Null);
        }

        [Test]
        public void RestartDeck_RefillsEmptyShoe()
        {
            StockOnly(-1);
            Assert.That(_deck.GetRandomCard(), Is.Null);

            _deck.RestartDeck();

            Assert.That(_deck.cardDictionary.Values, Has.All.EqualTo(4));
        }

        [Test]
        public void DealCard_FirstCard_AddsToSumAndKeepsTurn()
        {
            Seat(1, false, 0);
            Seat(2, false, 0);
            _deck.playerTurn.Value = 1;
            StockOnly(7);

            OfflineNetcode.ExpectRpcCalls(1); // HandResultClientRpc
            DealCard(1, true);

            Assert.That(_deck.playerInCurrentGameList[1], Is.EqualTo((false, 7)));
            Assert.That(_deck.playerTurn.Value, Is.EqualTo(1UL));
            Assert.That(_deck.cardDictionary.Single(kv => kv.Key.cardValue == 7).Value, Is.EqualTo(3));
        }

        [Test]
        public void DealCard_NormalDraw_PassesTurnToNextSeat()
        {
            Seat(1, false, 5);
            Seat(2, false, 5);
            _deck.playerTurn.Value = 1;
            StockOnly(3);

            OfflineNetcode.ExpectRpcCalls(1);
            DealCard(1, false);

            Assert.That(_deck.playerInCurrentGameList[1], Is.EqualTo((false, 8)));
            Assert.That(_deck.playerTurn.Value, Is.EqualTo(2UL));
        }

        [Test]
        public void DealCard_Exactly21_AwardsBlackjackPoint()
        {
            Seat(7, false, 11);
            Seat(8, false, 4);
            StockOnly(10);

            OfflineNetcode.ExpectRpcCalls(2); // HandResultClientRpc + "got a Blackjack" message
            DealCard(7, false);

            Assert.That(_deck.playerInGameList[7], Is.EqualTo(1));
            Assert.That(_deck.playerInGameList[8], Is.EqualTo(0));
        }

        [Test]
        public void DealCard_Bust_MarksPlayerDoneAndPassesTurn()
        {
            Seat(1, false, 15);
            Seat(2, false, 10);
            _deck.playerTurn.Value = 1;
            StockOnly(10);

            OfflineNetcode.ExpectRpcCalls(2); // HandResultClientRpc + "busted" message
            DealCard(1, false);

            Assert.That(_deck.playerInCurrentGameList[1], Is.EqualTo((true, 25)));
            Assert.That(_deck.playerTurn.Value, Is.EqualTo(2UL));
            Assert.That(_deck.playerInGameList[1], Is.EqualTo(0));
        }

        [Test]
        public void DealCard_EmptyShoe_LeavesHandUntouched()
        {
            Seat(1, false, 12);
            StockOnly(-1);

            OfflineNetcode.ExpectRpcCalls(2); // "Deck is empty!" + HandResultClientRpc(deckEmpty)
            DealCard(1, false);

            Assert.That(_deck.playerInCurrentGameList[1], Is.EqualTo((false, 12)));
        }

        [Test]
        public void AdvanceTurn_NonContiguousClientIds_RotatesBySeatNotId()
        {
            // Regression: the rotation used to be (playerTurn + 1) % count, so ids 0 and 2 threw
            // KeyNotFoundException looking up client 1.
            Seat(0, false, 0);
            Seat(2, false, 0);
            _deck.playerTurn.Value = 0;

            Reflect.Call(_deck, "AdvanceTurn", 0UL);
            Assert.That(_deck.playerTurn.Value, Is.EqualTo(2UL));

            Reflect.Call(_deck, "AdvanceTurn", 2UL);
            Assert.That(_deck.playerTurn.Value, Is.EqualTo(0UL));
        }

        [Test]
        public void AdvanceTurn_SkipsPlayersWhoAlreadyStood()
        {
            Seat(10, false, 0);
            Seat(11, true, 18);
            Seat(12, false, 0);

            Reflect.Call(_deck, "AdvanceTurn", 10UL);
            Assert.That(_deck.playerTurn.Value, Is.EqualTo(12UL));

            Reflect.Call(_deck, "AdvanceTurn", 12UL);
            Assert.That(_deck.playerTurn.Value, Is.EqualTo(10UL));
        }

        [Test]
        public void CheckIfAllPlayersDone_HighestStandingHandWins_BustedNeverWins()
        {
            Seat(1, true, 19);
            Seat(2, true, 24);
            Seat(3, true, 17);

            OfflineNetcode.ExpectRpcCalls(1); // winner message
            Reflect.Call(_deck, "CheckIfAllPlayersDone");

            Assert.That(_deck.playerInGameList[1], Is.EqualTo(1));
            Assert.That(_deck.playerInGameList[2], Is.EqualTo(0));
            Assert.That(_deck.playerInGameList[3], Is.EqualTo(0));
        }

        [Test]
        public void CheckIfAllPlayersDone_TiedHighScore_AllTiedPlayersWin()
        {
            Seat(1, true, 18);
            Seat(2, true, 18);
            Seat(3, true, 12);

            OfflineNetcode.ExpectRpcCalls(1);
            Reflect.Call(_deck, "CheckIfAllPlayersDone");

            Assert.That(_deck.playerInGameList[1], Is.EqualTo(1));
            Assert.That(_deck.playerInGameList[2], Is.EqualTo(1));
            Assert.That(_deck.playerInGameList[3], Is.EqualTo(0));
        }

        [Test]
        public void CheckIfAllPlayersDone_EveryoneBusted_NoWinner()
        {
            Seat(1, true, 22);
            Seat(2, true, 30);

            OfflineNetcode.ExpectRpcCalls(1); // "No winners this round"
            Reflect.Call(_deck, "CheckIfAllPlayersDone");

            Assert.That(_deck.playerInGameList.Values, Has.All.EqualTo(0));
        }

        [Test]
        public void CheckIfAllPlayersDone_SomeoneStillToAct_AwardsNothing()
        {
            Seat(1, true, 20);
            Seat(2, false, 5);

            Reflect.Call(_deck, "CheckIfAllPlayersDone");

            Assert.That(_deck.playerInGameList.Values, Has.All.EqualTo(0));
        }

        [Test]
        public void RemoveFromGame_LastPlayer_ParksTurnOnNoPlayer()
        {
            Seat(3, false, 9);
            _deck.playerTurn.Value = 3;

            Reflect.Call(_deck, "RemoveFromGame", 3UL);

            Assert.That(SeatOrder, Is.Empty);
            Assert.That(_deck.playerInCurrentGameList, Is.Empty);
            Assert.That(_deck.playerTurn.Value, Is.EqualTo(CardDeck.NoPlayer));
        }

        [Test]
        public void RemoveFromGame_PlayerWhoseTurnItWas_HandsTurnOn()
        {
            Seat(1, false, 0);
            Seat(2, false, 0);
            Seat(3, false, 0);
            _deck.playerTurn.Value = 2;

            Reflect.Call(_deck, "RemoveFromGame", 2UL);

            Assert.That(SeatOrder, Is.EqualTo(new ulong[] { 1, 3 }));
            Assert.That(_deck.playerTurn.Value, Is.Not.EqualTo(2UL));
            Assert.That(_deck.playerInCurrentGameList.ContainsKey(_deck.playerTurn.Value), Is.True);
        }

        [Test]
        public void RemoveFromGame_UnseatedClient_IsIgnored()
        {
            Seat(1, false, 4);
            _deck.playerTurn.Value = 1;

            Assert.DoesNotThrow(() => Reflect.Call(_deck, "RemoveFromGame", 99UL));
            Assert.That(SeatOrder, Is.EqualTo(new ulong[] { 1 }));
            Assert.That(_deck.playerTurn.Value, Is.EqualTo(1UL));
        }

        [Test]
        public void FirstFreeSeat_ReturnsLowestUnclaimedChair()
        {
            var seatByClient = Reflect.Get<Dictionary<ulong, int>>(_deck, "_seatByClient");
            seatByClient[5] = 0;
            seatByClient[9] = 2;

            Assert.That((int)Reflect.Call(_deck, "FirstFreeSeat", 4), Is.EqualTo(1));

            seatByClient[11] = 1;
            Assert.That((int)Reflect.Call(_deck, "FirstFreeSeat", 4), Is.EqualTo(3));
        }
    }

    [TestFixture, Category("Unit")]
    public class BlackJackHandTests
    {
        private GameObject _deckGo, _playerGo;
        private CardDeck _previousInstance;
        private BlackJack _player;

        [SetUp]
        public void SetUp()
        {
            _previousInstance = CardDeck.instance;
            _deckGo = new GameObject("CardDeck (unit test)") { hideFlags = HideFlags.DontSave };
            CardDeck deck = _deckGo.AddComponent<CardDeck>();
            deck.cardDeck = CardDeckTests.LoadAuthoredDeck();
            CardDeck.instance = deck;

            _playerGo = new GameObject("BlackJack (unit test)") { hideFlags = HideFlags.DontSave };
            _player = _playerGo.AddComponent<BlackJack>();
            _player.hand = new List<Card>();
        }

        [TearDown]
        public void TearDown()
        {
            CardDeck.instance = _previousInstance;
            Object.DestroyImmediate(_playerGo);
            Object.DestroyImmediate(_deckGo);
        }

        [Test]
        public void OnSeated_ResetsHandAndAllowsDrawOnly()
        {
            _player.hand.Add(CardDeck.instance.cardDeck[0]);
            _player.OnSeated();

            Assert.That(_player.IsSeated, Is.True);
            Assert.That(_player.hand, Is.Empty);
            Assert.That(_player.canDraw, Is.True);
            Assert.That(_player.canDone, Is.False);
        }

        [Test]
        public void ReceiveDealtCard_UnderTwentyOne_CanDrawAndStand()
        {
            _player.OnSeated();
            _player.ReceiveDealtCard(4, 5, false);

            Assert.That(_player.hand, Has.Count.EqualTo(1));
            Assert.That(_player.hand[0], Is.SameAs(CardDeck.instance.cardDeck[4]));
            Assert.That(_player.canDraw, Is.True);
            Assert.That(_player.canDone, Is.True);
        }

        [TestCase(21)]
        [TestCase(26)]
        public void ReceiveDealtCard_DecidedHand_LocksInput(int sum)
        {
            _player.OnSeated();
            _player.ReceiveDealtCard(9, sum, false);

            Assert.That(_player.canDraw, Is.False);
            Assert.That(_player.canDone, Is.False);
        }

        [Test]
        public void ReceiveDealtCard_DeckEmpty_OnlyStandAllowed()
        {
            _player.OnSeated();
            _player.ReceiveDealtCard(0, 0, true);

            Assert.That(_player.hand, Is.Empty);
            Assert.That(_player.canDraw, Is.False);
            Assert.That(_player.canDone, Is.True);
        }

        [TestCase(-1)]
        [TestCase(10)]
        [TestCase(999)]
        public void ReceiveDealtCard_OutOfRangeIndex_IgnoresCardButKeepsServerSum(int index)
        {
            _player.OnSeated();
            Assert.DoesNotThrow(() => _player.ReceiveDealtCard(index, 12, false));
            Assert.That(_player.hand, Is.Empty);
            Assert.That(_player.canDraw, Is.True);
            Assert.That(_player.canDone, Is.False, "cannot stand on an empty hand");
        }

        [Test]
        public void RestartHand_OnlySeatedPlayersGetBackIntoPlay()
        {
            _player.RestartHand();
            Assert.That(_player.canDraw, Is.False);

            _player.OnSeated();
            _player.ReceiveDealtCard(9, 25, false);
            _player.RestartHand();
            Assert.That(_player.hand, Is.Empty);
            Assert.That(_player.canDraw, Is.True);
            Assert.That(_player.canDone, Is.False);
        }

        [Test]
        public void OnLeftTable_ClearsSeatAndLocksKeys()
        {
            _player.OnSeated();
            _player.ReceiveDealtCard(2, 3, false);
            _player.OnLeftTable();

            Assert.That(_player.IsSeated, Is.False);
            Assert.That(_player.hand, Is.Empty);
            Assert.That(_player.canDraw, Is.False);
            Assert.That(_player.canDone, Is.False);
        }
    }
}
