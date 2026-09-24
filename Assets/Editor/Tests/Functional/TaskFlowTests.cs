using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace DuckRoulette.Tests.Functional
{
    // TaskManager's server-side round flow against a live host (see HostSessionFixture): carry-over,
    // the two-round swap, group break-up and the shared world state.
    [TestFixture, Category("Functional")]
    public class TaskFlowTests : HostSessionFixture
    {
        private readonly List<Challenge> _created = new();
        private Dictionary<Challenge, int> _registered;
        private List<KeyValuePair<Challenge, int>> _registeredSnapshot;

        private NetworkList<TaskManager.TaskEntry> Assigned => Reflect.Get<NetworkList<TaskManager.TaskEntry>>(Tm, "assignedTasks");

        [UnitySetUp]
        public IEnumerator TaskSetUp()
        {
            _registered = Reflect.Get<Dictionary<Challenge, int>>(typeof(TaskManager), "registeredObjectives");
            _registeredSnapshot = new List<KeyValuePair<Challenge, int>>(_registered);
            _registered.Clear();

            // Targeted ClientRpcs to the fake ids below never reach anyone; Netcode may complain.
            LogAssert.ignoreFailingMessages = true;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TaskTearDown()
        {
            Assigned.Clear();
            _registered.Clear();
            foreach (var pair in _registeredSnapshot) _registered[pair.Key] = pair.Value;
            foreach (Challenge c in _created) Object.Destroy(c);
            _created.Clear();
            LogAssert.ignoreFailingMessages = false;
            yield break;
        }

        private Challenge[] Tasks(params (string name, Challenge.TaskType type)[] specs)
        {
            var list = new Challenge[specs.Length];
            for (int i = 0; i < specs.Length; i++)
            {
                var c = ScriptableObject.CreateInstance<Challenge>();
                c.name = c.taskName = specs[i].name;
                c.taskType = specs[i].type;
                _created.Add(c);
                TaskManager.RegisterObjective(c);
                list[i] = c;
            }

            Tm.tasks = list;
            Reflect.Call(Tm, "RebuildTaskIndices");
            return list;
        }

        private List<TaskManager.TaskEntry> EntriesFor(ulong clientId)
        {
            var result = new List<TaskManager.TaskEntry>();
            foreach (TaskManager.TaskEntry e in Assigned)
            {
                if (e.ClientId == clientId) result.Add(e);
            }
            return result;
        }

        [UnityTest]
        public IEnumerator HandOff_DropsFinishedTasks_AndCarriesOpenOnesOver()
        {
            Tasks(("A", Challenge.TaskType.Useful), ("B", Challenge.TaskType.Useless));
            Assigned.Add(new TaskManager.TaskEntry { ClientId = Host, TaskIndex = 0 });
            Assigned.Add(new TaskManager.TaskEntry { ClientId = 7, TaskIndex = 1, Completed = true });

            Tm.OnGunHandedOff();

            Assert.That(EntriesFor(7), Is.Empty, "a finished task is cleared at the hand-off");
            List<TaskManager.TaskEntry> open = EntriesFor(Host);
            Assert.That(open, Has.Count.EqualTo(1));
            Assert.That(open[0].TaskIndex, Is.EqualTo(0), "an unfinished task is kept, not re-rolled");
            Assert.That(open[0].RoundsOpen, Is.EqualTo(1));
            Assert.That(Tm.HasCompletedAllTasks(Host), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HandOff_TaskOpenForTwoRounds_IsSwappedForADifferentOne()
        {
            Tasks(("A", Challenge.TaskType.Useful), ("B", Challenge.TaskType.Useless));
            Assigned.Add(new TaskManager.TaskEntry { ClientId = Host, TaskIndex = 0, RoundsOpen = 1 });

            Tm.OnGunHandedOff();

            List<TaskManager.TaskEntry> open = EntriesFor(Host);
            Assert.That(open, Has.Count.EqualTo(1), "still exactly one task");
            Assert.That(open[0].TaskIndex, Is.EqualTo(1));
            Assert.That(open[0].RoundsOpen, Is.EqualTo(0));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerRemoved_GroupBelowThree_RemainingMembersGetDistinctSoloTasks()
        {
            Tasks(("Group", Challenge.TaskType.ThreePlus), ("A", Challenge.TaskType.Useful), ("B", Challenge.TaskType.Useless));
            foreach (ulong id in new ulong[] { Host, 5, 6 })
            {
                Assigned.Add(new TaskManager.TaskEntry { ClientId = id, TaskIndex = 0, GroupId = 9 });
            }

            Tm.OnPlayerRemoved(6);

            Assert.That(EntriesFor(6), Is.Empty);
            List<TaskManager.TaskEntry> host = EntriesFor(Host), other = EntriesFor(5);
            Assert.That(host, Has.Count.EqualTo(1));
            Assert.That(other, Has.Count.EqualTo(1));
            Assert.That(host[0].IsGroup || other[0].IsGroup, Is.False, "the group is broken up");
            Assert.That(host[0].TaskIndex, Is.Not.EqualTo(other[0].TaskIndex), "two players never share a solo task");
            Assert.That(new[] { host[0].TaskIndex, other[0].TaskIndex }, Is.EquivalentTo(new[] { 1, 2 }));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerRemoved_GroupStillThree_IsKept()
        {
            Tasks(("Group", Challenge.TaskType.ThreePlus), ("A", Challenge.TaskType.Useful));
            foreach (ulong id in new ulong[] { Host, 5, 6, 8 })
            {
                Assigned.Add(new TaskManager.TaskEntry { ClientId = id, TaskIndex = 0, GroupId = 9 });
            }

            Tm.OnPlayerRemoved(8);

            Assert.That(Assigned.Count, Is.EqualTo(3));
            foreach (TaskManager.TaskEntry e in Assigned) Assert.That(e.GroupId, Is.EqualTo(9));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Completion_IsSharedWorldState_UntilHandedOutAgain()
        {
            Challenge[] tasks = Tasks(("Campfire", Challenge.TaskType.Useless), ("Other", Challenge.TaskType.Useful));
            Assigned.Add(new TaskManager.TaskEntry { ClientId = Host, TaskIndex = 0 });

            Tm.CompleteTaskForPlayer(Host, tasks[0]);
            Assert.That(Tm.IsTaskDoneInWorld(tasks[0]), Is.True);
            Assert.That(Tm.HasCompletedAllTasks(Host), Is.True);

            // Handing the same task to someone else puts it back in its unfinished state.
            Tm.OnGunHandedOff();
            Assigned.Add(new TaskManager.TaskEntry { ClientId = 5, TaskIndex = 1, RoundsOpen = 1 });
            Reflect.Call(Tm, "SwapToSoloTask", 5UL);
            Assert.That(EntriesFor(5)[0].TaskIndex, Is.EqualTo(0));
            Assert.That(Tm.IsTaskDoneInWorld(tasks[0]), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CompleteForAllHolders_FinishesTheWholeGroup()
        {
            Challenge[] tasks = Tasks(("Push", Challenge.TaskType.ThreePlus));
            foreach (ulong id in new ulong[] { Host, 5, 6 })
            {
                Assigned.Add(new TaskManager.TaskEntry { ClientId = id, TaskIndex = 0, GroupId = 2 });
            }

            Tm.CompleteTaskForAllHolders(tasks[0]);

            foreach (ulong id in new ulong[] { Host, 5, 6 }) Assert.That(Tm.HasCompletedAllTasks(id), Is.True);
            yield return null;
        }
    }
}
