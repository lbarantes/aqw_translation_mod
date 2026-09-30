using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AQWMod.Localization.Sync
{
    public sealed class EntryConflict
    {
        public string File { get; set; } = "";
        public string Key { get; set; } = "";
        /// <summary>Versão do usuário (null = ele apagou a entrada).</summary>
        public JToken? Local { get; set; }
        /// <summary>Versão do GitHub (null = o GitHub apagou a entrada).</summary>
        public JToken? Remote { get; set; }
    }

    public sealed class MergeResult
    {
        /// <summary>Entradas resultantes, já na ordem final (ordem do remoto, depois as só-locais).</summary>
        public JObject Merged { get; } = new JObject();
        public List<EntryConflict> Conflicts { get; } = new List<EntryConflict>();

        public int Added { get; set; }        // veio do remoto, não existia antes
        public int Updated { get; set; }      // remoto mudou e o usuário não tinha mexido
        public int Removed { get; set; }      // remoto apagou e o usuário não tinha mexido
        public int KeptLocal { get; set; }    // edição/adição/remoção só do usuário, preservada
    }

    // Merge 3-way por entrada (chave do JSON) entre:
    //   B = base   (como o arquivo veio do GitHub na última sincronização)
    //   L = local  (como está no disco agora)
    //   R = remoto (a versão nova do GitHub)
    //
    //   L == R            → nada a decidir
    //   L == B            → o usuário não mexeu: aceita R (inclusive adição/remoção)
    //   R == B            → o GitHub não mudou: mantém L (inclusive adição/remoção)
    //   os três diferentes → CONFLITO: o remoto vence no arquivo base e a versão do
    //                        usuário é devolvida em Conflicts pra ir pra overrides/
    //
    // Puro: só JObject em memória, sem disco nem Unity.
    public static class ThreeWayMerge
    {
        public static MergeResult Merge(string file, JObject baseEntries, JObject localEntries, JObject remoteEntries)
        {
            var result = new MergeResult();

            var keys = new List<string>(remoteEntries.Properties().Select(p => p.Name));
            var seen = new HashSet<string>(keys, StringComparer.Ordinal);
            foreach (var p in localEntries.Properties())
                if (seen.Add(p.Name)) keys.Add(p.Name);

            foreach (var key in keys)
            {
                var b = baseEntries[key];
                var l = localEntries[key];
                var r = remoteEntries[key];

                if (EntryCanon.Same(l, r))
                {
                    // mesma entrada; a grafia do remoto é a referência
                    Put(result, key, r ?? l);
                }
                else if (EntryCanon.Same(l, b))
                {
                    if (r == null) result.Removed++;
                    else if (b == null) result.Added++;
                    else result.Updated++;
                    Put(result, key, r);
                }
                else if (EntryCanon.Same(r, b))
                {
                    result.KeptLocal++;
                    Put(result, key, l);
                }
                else
                {
                    result.Conflicts.Add(new EntryConflict
                    {
                        File   = file,
                        Key    = key,
                        Local  = l?.DeepClone(),
                        Remote = r?.DeepClone(),
                    });
                    Put(result, key, r); // remoto vence no arquivo base
                }
            }

            return result;
        }

        private static void Put(MergeResult result, string key, JToken? value)
        {
            if (value != null) result.Merged[key] = value.DeepClone();
        }
    }
}
