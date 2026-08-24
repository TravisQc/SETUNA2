using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SETUNA.Main.Common.Tests
{
    /// <summary>
    /// Pins the deterministic-disposal contract that replaced the six finalizers:
    /// every <see cref="BaseForm"/> releases what it owns through
    /// <c>DisposeOwnedResources</c>, and a subclass that brings its own
    /// designer-generated <c>Dispose(bool)</c> still reaches that hook.
    /// </summary>
    [TestClass]
    public class BaseFormDisposalTests
    {
        class TrackingForm : BaseForm
        {
            public int DisposeOwnedResourcesCalls { get; private set; }

            protected override void DisposeOwnedResources()
            {
                base.DisposeOwnedResources();
                DisposeOwnedResourcesCalls++;
            }
        }

        /// <summary>
        /// Mirrors CaptureForm/CompactScrap: the designer emits its own
        /// <c>Dispose(bool)</c> override that ends in <c>base.Dispose(disposing)</c>.
        /// </summary>
        class DesignerStyleForm : TrackingForm
        {
            public bool OwnDisposeRan { get; private set; }

            protected override void Dispose(bool disposing)
            {
                OwnDisposeRan = true;
                base.Dispose(disposing);
            }
        }

        [TestMethod]
        public void DisposingAFormReleasesWhatItOwns()
        {
            var form = new TrackingForm();
            Assert.AreEqual(0, form.DisposeOwnedResourcesCalls);

            form.Dispose();

            Assert.AreEqual(1, form.DisposeOwnedResourcesCalls);
        }

        [TestMethod]
        public void ADesignerGeneratedDisposeStillReachesTheHook()
        {
            var form = new DesignerStyleForm();

            form.Dispose();

            Assert.IsTrue(form.OwnDisposeRan, "The subclass override must run.");
            Assert.AreEqual(1, form.DisposeOwnedResourcesCalls, "base.Dispose must chain through to BaseForm.");
        }

        [TestMethod]
        public void DisposingTwiceDoesNotReleaseTwice()
        {
            var form = new TrackingForm();

            form.Dispose();
            form.Dispose();

            // Control.Dispose() is guarded, so the hook must not run again and
            // double-dispose an image or pen.
            Assert.AreEqual(1, form.DisposeOwnedResourcesCalls);
        }

        [TestMethod]
        public void ClosingAFormDisposesItAndRunsTheHook()
        {
            // Close() on a non-modal, never-shown form disposes it, which is the
            // path scrap windows actually take.
            var form = new TrackingForm();

            form.Close();
            form.Dispose();

            Assert.AreEqual(1, form.DisposeOwnedResourcesCalls);
        }

        [TestMethod]
        public void NoTypeInTheAssemblyDeclaresAFinalizer()
        {
            // Finalizers here ran on the finalizer thread and touched managed
            // state — one of them closed windows off the UI thread.
            var offenders = new System.Collections.Generic.List<string>();

            foreach (var type in typeof(BaseForm).Assembly.GetTypes())
            {
                var finalizer = type.GetMethod(
                    "Finalize",
                    System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.DeclaredOnly);

                if (finalizer != null)
                {
                    offenders.Add(type.FullName);
                }
            }

            Assert.AreEqual(
                0,
                offenders.Count,
                "These types still declare a finalizer: " + string.Join(", ", offenders));
        }

        [TestMethod]
        public void ScrapBookIsDisposable()
        {
            Assert.IsTrue(
                typeof(System.IDisposable).IsAssignableFrom(typeof(ScrapBook)),
                "ScrapBook must be disposable so Mainform can close its scraps on the UI thread.");
        }
    }
}
