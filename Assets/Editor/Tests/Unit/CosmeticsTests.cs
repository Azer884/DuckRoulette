using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace DuckRoulette.Tests.Unit
{
    // Cosmetics.ChangeHat/ChangeAccessorie/ChangeShirt write to Steam Remote Storage on every call, so
    // only the index -> object application (Change / ChangeCosmetic) is exercised here. Index 0 means
    // "nothing equipped"; index N equips item N-1.
    [TestFixture, Category("Unit")]
    public class CosmeticsTests
    {
        private readonly List<Object> _cleanup = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _cleanup.Count - 1; i >= 0; i--)
            {
                if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
            }
            _cleanup.Clear();
        }

        private GameObject NewGo(string name, Transform parent = null)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            if (parent != null) go.transform.SetParent(parent);
            else _cleanup.Add(go);
            return go;
        }

        private GameObject NewItem(string name)
        {
            GameObject item = NewGo(name);
            item.AddComponent<MeshRenderer>();
            return item;
        }

        // ---------- Cosmetics (menu / skins selector) ----------

        [Test]
        public void Cosmetics_Change_IndexZero_EquipsNothing()
        {
            Cosmetics cosmetics = NewGo("Cosmetics").AddComponent<Cosmetics>();
            Transform holder = NewGo("Holder").transform;
            var list = new List<GameObject> { NewItem("HatA"), NewItem("HatB") };

            Reflect.Call(cosmetics, "Change", list, holder, 0);

            Assert.That(holder.childCount, Is.Zero);
        }

        [TestCase(1, "HatA")]
        [TestCase(2, "HatB")]
        public void Cosmetics_Change_IndexN_EquipsItemNMinusOne(int index, string expected)
        {
            Cosmetics cosmetics = NewGo("Cosmetics").AddComponent<Cosmetics>();
            Transform holder = NewGo("Holder").transform;
            var list = new List<GameObject> { NewItem("HatA"), NewItem("HatB") };

            Reflect.Call(cosmetics, "Change", list, holder, index);

            Assert.That(holder.childCount, Is.EqualTo(1));
            Assert.That(holder.GetChild(0).name, Does.StartWith(expected));
        }

        // ---------- NetworkCosmetics (replicated on the player) ----------

        private static readonly Type[] InstantiateOverload = { typeof(GameObject[]), typeof(Transform), typeof(Transform), typeof(int) };
        private static readonly Type[] ToggleOverload = { typeof(GameObject[]), typeof(GameObject[]), typeof(int) };

        private NetworkCosmetics NewNetworkCosmetics()
        {
            Assume.That(NetworkManager.Singleton, Is.Null);
            return NewGo("NetworkCosmetics").AddComponent<NetworkCosmetics>();
        }

        [Test]
        public void NetworkCosmetics_InstantiateMode_IndexZeroClearsSlot()
        {
            NetworkCosmetics nc = NewNetworkCosmetics();
            Transform parent = NewGo("HatSlot").transform;
            GameObject[] items = { NewItem("HatA") };

            Reflect.CallTyped(nc, "ChangeCosmetic", InstantiateOverload, items, null, parent, 0);

            Assert.That(parent.childCount, Is.Zero);
        }

        [Test]
        public void NetworkCosmetics_InstantiateMode_EquipsOnRemoteLayerWithoutShadowRig()
        {
            NetworkCosmetics nc = NewNetworkCosmetics();
            Transform parent = NewGo("HatSlot").transform;
            GameObject[] items = { NewItem("HatA"), NewItem("HatB") };

            // shadowParent null = avatar without a first-person shadow double (lobby Character prefab).
            Assert.DoesNotThrow(() => Reflect.CallTyped(nc, "ChangeCosmetic", InstantiateOverload, items, null, parent, 2));

            Assert.That(parent.childCount, Is.EqualTo(1));
            Assert.That(parent.GetChild(0).name, Does.StartWith("HatB"));
            Assert.That(parent.GetChild(0).gameObject.layer, Is.EqualTo(3), "non-owner copies render on layer 3");
        }

        [Test]
        public void NetworkCosmetics_InstantiateMode_ShadowCopyIsShadowsOnly()
        {
            NetworkCosmetics nc = NewNetworkCosmetics();
            Transform parent = NewGo("HatSlot").transform;
            Transform shadowParent = NewGo("ShadowHatSlot").transform;
            GameObject[] items = { NewItem("HatA") };

            Reflect.CallTyped(nc, "ChangeCosmetic", InstantiateOverload, items, shadowParent, parent, 1);

            Assert.That(shadowParent.childCount, Is.EqualTo(1));
            Renderer shadowRenderer = shadowParent.GetChild(0).GetComponent<Renderer>();
            Assert.That(shadowRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.ShadowsOnly));
            Assert.That(shadowParent.GetChild(0).gameObject.layer, Is.EqualTo(2));
            Assert.That(parent.GetChild(0).GetComponent<Renderer>().shadowCastingMode, Is.EqualTo(ShadowCastingMode.On));
        }

        [Test]
        public void NetworkCosmetics_ToggleMode_ActivatesItemAndShadow_ToleratesShortShadowArray()
        {
            NetworkCosmetics nc = NewNetworkCosmetics();
            GameObject shirtA = NewItem("ShirtA"), shirtB = NewItem("ShirtB");
            shirtA.SetActive(false);
            shirtB.SetActive(false);
            GameObject shadowA = NewItem("ShadowShirtA");
            shadowA.SetActive(false);

            Reflect.CallTyped(nc, "ChangeCosmetic", ToggleOverload, new[] { shirtA, shirtB }, new[] { shadowA }, 1);
            Assert.That(shirtA.activeSelf, Is.True);
            Assert.That(shadowA.activeSelf, Is.True);
            Assert.That(shadowA.GetComponent<Renderer>().shadowCastingMode, Is.EqualTo(ShadowCastingMode.ShadowsOnly));

            // shadowShirts shorter than shirts must not index out of range.
            Assert.DoesNotThrow(() => Reflect.CallTyped(nc, "ChangeCosmetic", ToggleOverload, new[] { shirtA, shirtB }, new[] { shadowA }, 2));
            Assert.That(shirtB.activeSelf, Is.True);
        }

        // ChangeCosmetic itself has no bounds check; it relies on ChangeNetVarsServerRpc
        // (NetworkCosmetics.cs:132-134) clamping to 0..items.Length via RpcValidation.SanitizeCosmeticIndex
        // (covered in Security/RpcValidationTests). This pins the upper edge of that contract.
        [Test]
        public void NetworkCosmetics_HighestSanitizedIndex_EquipsLastItem()
        {
            NetworkCosmetics nc = NewNetworkCosmetics();
            Transform parent = NewGo("HatSlot").transform;
            GameObject[] items = { NewItem("HatA"), NewItem("HatB") };
            int highest = items.Length;
            Assume.That(RpcValidation.SanitizeCosmeticIndex(highest, items.Length), Is.EqualTo(highest));

            Assert.DoesNotThrow(() => Reflect.CallTyped(nc, "ChangeCosmetic", InstantiateOverload, items, null, parent, highest));
            Assert.That(parent.GetChild(0).name, Does.StartWith("HatB"));
        }

        // Cosmetics.LoadCosmeticIndexes (Cosmetics.cs:120-122) parses indices from Steam Cloud's
        // cosmeticData.txt with no range check and hands them to Change (Cosmetics.cs:143-145 -> :89).
        // A save made when the selector listed more items (content removed, or a different selector's
        // list) or a hand-edited file throws ArgumentOutOfRangeException and aborts cosmetics loading.
        [TestCase(3)]
        [TestCase(-1)]
        public void Cosmetics_Change_OutOfRangeSavedIndex_DoesNotThrow(int index)
        {
            Cosmetics cosmetics = NewGo("Cosmetics").AddComponent<Cosmetics>();
            Transform holder = NewGo("Holder").transform;
            var list = new List<GameObject> { NewItem("HatA"), NewItem("HatB") };

            Assert.DoesNotThrow(() => Reflect.Call(cosmetics, "Change", list, holder, index));
        }
    }
}
