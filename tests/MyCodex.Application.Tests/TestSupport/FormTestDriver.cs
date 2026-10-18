using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests.TestSupport
{
    /// <summary>
    /// Shared reflection and control-tree access for tests that drive forms, controls, and services
    /// through members that are not public. Keep reflection here instead of copying helpers per test file.
    /// </summary>
    internal static class FormTestDriver
    {
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.NonPublic;

        public static T Field<T>(object instance, string name)
        {
            return (T)FindField(instance, name).GetValue(instance);
        }

        public static void SetField(object instance, string name, object value)
        {
            FindField(instance, name).SetValue(instance, value);
        }

        public static object Invoke(object instance, string methodName, params object[] args)
        {
            return FindMethod(instance.GetType(), methodName, InstanceMembers).Invoke(instance, args);
        }

        public static T Invoke<T>(object instance, string methodName, params object[] args)
        {
            return (T)Invoke(instance, methodName, args);
        }

        /// <summary>Invokes a non-public method that returns a Task and blocks until it completes.</summary>
        public static void InvokeAndWait(object instance, string methodName, params object[] args)
        {
            var task = Invoke(instance, methodName, args) as Task;
            Assert.That(task, Is.Not.Null, methodName + " did not return a Task.");
            task.GetAwaiter().GetResult();
        }

        public static T InvokeStatic<T>(Type type, string methodName, params object[] args)
        {
            return (T)FindMethod(type, methodName, StaticMembers).Invoke(null, args);
        }

        public static bool ProcessCmdKey(Form form, Keys keyData)
        {
            var message = new Message();
            return (bool)FindMethod(typeof(Form), "ProcessCmdKey", InstanceMembers).Invoke(form, new object[] { message, keyData });
        }

        public static void ClickOk(Form form)
        {
            FindMethod(form.GetType(), "OkButton_Click", InstanceMembers).Invoke(form, new object[] { null, EventArgs.Empty });
        }

        /// <summary>Returns every descendant of the root in depth-first order, excluding the root itself.</summary>
        public static IEnumerable<T> Descendants<T>(Control root) where T : Control
        {
            foreach (Control child in root.Controls)
            {
                var typed = child as T;
                if (typed != null)
                {
                    yield return typed;
                }

                foreach (var nested in Descendants<T>(child))
                {
                    yield return nested;
                }
            }
        }

        public static T Single<T>(Control root) where T : Control
        {
            return Descendants<T>(root).Single();
        }

        public static T FindControlOrDefault<T>(Control root, string accessibleName) where T : Control
        {
            return Descendants<T>(root).FirstOrDefault(control => string.Equals(control.AccessibleName, accessibleName, StringComparison.Ordinal));
        }

        public static T FindControl<T>(Control root, string accessibleName) where T : Control
        {
            var control = FindControlOrDefault<T>(root, accessibleName);
            Assert.That(control, Is.Not.Null, typeof(T).Name + " with accessible name " + accessibleName + " was not found.");
            return control;
        }

        private static FieldInfo FindField(object instance, string name)
        {
            Assert.That(instance, Is.Not.Null, "Instance for field " + name + " is null.");
            for (var type = instance.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, InstanceMembers | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }
            }

            Assert.Fail("Non-public field not found: " + instance.GetType().Name + "." + name);
            return null;
        }

        private static MethodInfo FindMethod(Type type, string name, BindingFlags flags)
        {
            var method = type.GetMethod(name, flags);
            Assert.That(method, Is.Not.Null, "Non-public method not found: " + type.Name + "." + name);
            return method;
        }
    }
}
