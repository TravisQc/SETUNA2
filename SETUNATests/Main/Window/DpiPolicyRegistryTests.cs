using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SETUNA.Main.Tests;
using SETUNA.Main.Window;

namespace SETUNATests.Main.Window
{
    [TestClass]
    public class DpiPolicyRegistryTests
    {
        [TestMethod]
        public void EveryConcreteApplicationBaseFormHasOneExplicitPolicy()
        {
            var forms = typeof(BaseForm).Assembly
                .GetTypes()
                .Where(type => type != typeof(BaseForm)
                    && typeof(BaseForm).IsAssignableFrom(type)
                    && !type.IsAbstract
                    && !type.ContainsGenericParameters)
                .ToArray();

            Assert.IsTrue(forms.Length > 0, "No BaseForm-derived application forms were discovered.");

            foreach (var form in forms)
            {
                DpiPolicy policy;
                Assert.IsTrue(
                    DpiPolicyRegistry.TryGetPolicy(form, out policy),
                    "Missing explicit DPI policy for " + form.FullName);
                Assert.IsTrue(
                    policy == DpiPolicy.LogicalUi || policy == DpiPolicy.PhysicalSurface,
                    "Invalid DPI policy for " + form.FullName);
            }
        }

        [TestMethod]
        public void LogicalAndPhysicalRepresentativeFormsAreClassifiedCorrectly()
        {
            Assert.AreEqual(DpiPolicy.LogicalUi, DpiPolicyRegistry.GetPolicy(typeof(SETUNA.Mainform)));
            Assert.AreEqual(DpiPolicy.LogicalUi, DpiPolicyRegistry.GetPolicy(typeof(SETUNA.Main.Option.OptionForm)));
            Assert.AreEqual(DpiPolicy.PhysicalSurface, DpiPolicyRegistry.GetPolicy(typeof(SETUNA.Main.ScrapBase)));
            Assert.AreEqual(DpiPolicy.PhysicalSurface, DpiPolicyRegistry.GetPolicy(typeof(SETUNA.Main.CaptureForm)));
        }

        /// <summary>
        /// A form may also state its policy locally by overriding <c>BaseForm.DpiPolicy</c>,
        /// and 14 do. The registry wins when they disagree, which would make the local
        /// declaration a lie that reads as the truth — so they are not allowed to disagree.
        /// <para>
        /// Only the constructible forms can be checked: reading a virtual property needs an
        /// instance, and the physical surfaces need a canvas this host cannot supply. Their
        /// classification is covered instead by the completeness test above plus the
        /// dual-monitor matrix.
        /// </para>
        /// </summary>
        [TestMethod]
        public void EveryFormsLocalDeclarationAgreesWithTheRegistry()
        {
            var declaration = typeof(BaseForm).GetProperty(
                "DpiPolicy", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(declaration, "BaseForm no longer exposes a local policy declaration.");

            var disagreements = new List<string>();

            foreach (var form in ApplicationForms.All())
            {
                using (form)
                {
                    if (!(form is BaseForm window))
                    {
                        continue;
                    }

                    var local = (DpiPolicy)declaration.GetValue(window);
                    if (local != window.Policy)
                    {
                        disagreements.Add(
                            form.GetType().Name + " declares " + local + " but the registry says " + window.Policy);
                    }
                }
            }

            Assert.AreEqual(
                0,
                disagreements.Count,
                "The registry is authoritative, so a disagreeing override is silently ignored:"
                    + Environment.NewLine + string.Join(Environment.NewLine, disagreements));
        }
    }
}
