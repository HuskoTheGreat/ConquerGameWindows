using System;
using System.IO;
using System.Security.Cryptography;
using Conquer.Launcher;

namespace Conquer.UpdateSigner
{
    /// <summary>
    /// Creates the release signing key and signs update manifests. See installer/SIGNING.md.
    ///
    ///   keygen PUBLIC_KEY_FILE PRIVATE_KEY_FILE   new P-256 key: base64 public key, PEM private key
    ///   sign MANIFEST PUBLIC_KEY_FILE OUTPUT      signs MANIFEST with the PEM key in $UPDATE_SIGNING_KEY into OUTPUT
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            try
            {
                if (args.Length == 3 && args[0] == "keygen") return KeyGen(args[1], args[2]);
                if (args.Length == 4 && args[0] == "sign") return Sign(args[1], args[2], args[3]);
                Console.Error.WriteLine("Usage: keygen PUBLIC_KEY_FILE PRIVATE_KEY_FILE | sign MANIFEST PUBLIC_KEY_FILE OUTPUT");
                return 2;
            }
            catch (Exception e) when (e is IOException || e is CryptographicException || e is ArgumentException || e is InvalidDataException)
            {
                Console.Error.WriteLine("error: " + e.Message);
                return 1;
            }
        }

        static int KeyGen(string publicFile, string privateFile)
        {
            if (File.Exists(privateFile)) throw new IOException($"{privateFile} already exists; not overwriting a key.");
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            File.WriteAllText(publicFile, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) + Environment.NewLine);
            File.WriteAllText(privateFile, key.ExportPkcs8PrivateKeyPem() + Environment.NewLine);
            Console.WriteLine($"Public key written to {publicFile}");
            Console.WriteLine($"Private key written to {privateFile}");
            return 0;
        }

        static int Sign(string manifestFile, string publicFile, string outputFile)
        {
            string pem = Environment.GetEnvironmentVariable("UPDATE_SIGNING_KEY");
            if (string.IsNullOrWhiteSpace(pem)) throw new ArgumentException("UPDATE_SIGNING_KEY is not set.");

            byte[] manifest = File.ReadAllBytes(manifestFile);
            using var key = ECDsa.Create();
            key.ImportFromPem(pem);
            byte[] signed = System.Text.Encoding.UTF8.GetBytes(SignedManifest.Sign(manifest, key).ToJson());

            // Verify exactly as a launcher built with this public key would, so a mismatched key fails here.
            byte[] publicKey = Convert.FromBase64String(File.ReadAllText(publicFile).Trim());
            UpdateManifest checkedManifest = SignedManifest.Verify(signed, publicKey);
            File.WriteAllBytes(outputFile, signed);
            Console.WriteLine($"Signed {checkedManifest.Version} (build {checkedManifest.Build}) into {outputFile}");
            return 0;
        }
    }
}
