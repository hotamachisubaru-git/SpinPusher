// Synaptic AI Pro for Unity - InstanceID/EntityId Compatibility Helper
//
// Unity 6 系で `Object.GetInstanceID()` / `EditorUtility.InstanceIDToObject()` /
// `SerializedProperty.objectReferenceInstanceIDValue` が `[Obsolete(IsError=true)]` 化され
// CS0619 (= 抑制不能なハードエラー) を出すようになった。後継 API:
//   - `GetEntityId()`            (UnityEngine.Object)
//   - `EntityIdToObject(UnityEngine.EntityId)` (UnityEditor.EditorUtility)
//   - `objectReferenceEntityIdValue` (UnityEditor.SerializedProperty)
// は Unity 6.5+ でのみ利用可能。Unity 2022.3 LTS / 6.0-6.4 LTS との両対応を維持するため、
// バージョン分岐したラッパーを 1 箇所に集約する。
//
// v1.2.28 (2026-09-19): Unity 6.5+ の EntityId ハンドルテーブル方式に置き換え。
//   従来: `o.GetEntityId().GetHashCode()` で int 化 → `EntityIdToObject(int)` で復元
//         → 6.5+ で int↔EntityId 変換が「意図的に」遮断されたため復元不能
//         (Unity 公式移行ガイド: "avoid casting EntityId to/from an integer type")。
//         同種の問題は Animancer / FishNet / Unity 公式 Behavior でも発生。
//   現行: 我々自身のセッション内 int ハンドルを配布し、実 EntityId は Dictionary で保持。
//         復元時は保持した EntityId を EntityIdToObject(EntityId) に渡す (public API)。
//   制約: ハンドルは Editor セッション内で有効。domain reload で map がクリアされ、
//         復元不能なハンドル入力は null を返す (誤った Object を返すよりは安全)。
//         AI 編集セッション用途では実質支障なし。

using UnityEditor;
using UnityEngine;

#if UNITY_6000_5_OR_NEWER
using System.Collections.Generic;
#endif

namespace SynapticPro
{
    public static class IdCompat
    {
#if UNITY_6000_5_OR_NEWER
        // Unity 6.5+ ハンドルテーブル: int ハンドル ⇔ EntityId 実体の双方向マップ。
        // 同一 EntityId には常に同じハンドルを返す (`GetIdCompat` の等値性維持)。
        private static readonly Dictionary<UnityEngine.EntityId, int> _entityToHandle = new();
        private static readonly Dictionary<int, UnityEngine.EntityId> _handleToEntity = new();
        private static int _nextHandle = 1;
        private static readonly object _lock = new();

        private static int RegisterHandle(UnityEngine.EntityId eid)
        {
            lock (_lock)
            {
                if (_entityToHandle.TryGetValue(eid, out var h)) return h;
                h = _nextHandle++;
                _entityToHandle[eid] = h;
                _handleToEntity[h] = eid;
                return h;
            }
        }
#endif

        // Object.GetInstanceID() の代替
        public static int GetIdCompat(this Object o)
        {
#if UNITY_6000_5_OR_NEWER
            return RegisterHandle(o.GetEntityId());
#else
            return o.GetInstanceID();
#endif
        }

        // EditorUtility.InstanceIDToObject(int) の代替
        public static Object IdToObjectCompat(int id)
        {
#if UNITY_6000_5_OR_NEWER
            // int → EntityId 直接復元は Unity 6.5+ で API 上遮断されている。
            // 本セッションで発行済のハンドルは実 EntityId を map で保持しており、
            // それを EntityIdToObject(EntityId) に渡すことで正規に復元する。
            UnityEngine.EntityId eid;
            lock (_lock)
            {
                if (!_handleToEntity.TryGetValue(id, out eid)) return null;
            }
            return EditorUtility.EntityIdToObject(eid);
#else
            return EditorUtility.InstanceIDToObject(id);
#endif
        }

        // SerializedProperty.objectReferenceInstanceIDValue の代替
        public static int GetObjectReferenceIdCompat(this SerializedProperty sp)
        {
#if UNITY_6000_5_OR_NEWER
            return RegisterHandle(sp.objectReferenceEntityIdValue);
#else
            return sp.objectReferenceInstanceIDValue;
#endif
        }
    }
}
