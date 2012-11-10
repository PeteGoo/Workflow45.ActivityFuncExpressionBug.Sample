using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Activities;
using System.Activities.Presentation;
using System.Windows;
using Microsoft.VisualBasic.Activities;
using System.Reflection;
using System.Diagnostics;
using System.Activities.Expressions;
using LinqExpression = System.Linq.Expressions.Expression;
using System.Collections.ObjectModel;
using System.Activities.Validation;
using System.Timers;

namespace Workflow40QueryActivity {
    [Designer(typeof(QueryDesigner))]
    public class Query<T> : NativeActivity<IEnumerable<T>>, IActivityTemplateFactory {

        [RequiredArgument]
        public InArgument<IQueryable<T>> Source { get; set; }

        [RequiredArgument]
        public ActivityFunc<T, bool> Filter { get; set; }


        private Collection<RuntimeArgument> runtimeArguments = new Collection<RuntimeArgument>();

        protected override void CacheMetadata(NativeActivityMetadata metadata) {
            base.CacheMetadata(metadata);
            
            if (Filter == null || Filter.Handler == null || Filter.Handler.GetType().GetProperty("ExpressionText") == null || string.IsNullOrEmpty(Filter.Handler.GetType().GetProperty("ExpressionText").GetValue(Filter.Handler, null).ToString())) {
                metadata.AddValidationError(new ValidationError(string.Format("The Where clause must be set. Use the 'item' property to define your expression. {0}e.g. item.BusinessEntity.FirstName=\"John\"", Environment.NewLine), false, "Filter"));
            }
            else {

                string expressionText = Filter.Handler.GetType().GetProperty("ExpressionText").GetValue(Filter.Handler, null).ToString();
                runtimeArguments = BindArgumentsAndVariables(metadata, expressionText, runtimeArguments, DisplayName, Id);

                runtimeArguments.ToList().ForEach(
                    arg =>
                    Debug.WriteLine(string.Format("Binding argument to query {0}:{1}", arg.Name,
                                                    arg.Type.FullName)));
                
            }
        }

        

        /// <summary>
        /// When implemented in a derived class, performs the execution of the activity.
        /// </summary>
        /// <returns>
        /// The result of the activity’s execution.
        /// </returns>
        /// <param name="context">The execution context under which the activity executes.</param>
        protected override void Execute(NativeActivityContext context) {
            VisualBasicValue<bool> expression = Filter.Handler as VisualBasicValue<bool>;
            if (expression == null) {
                Result.Set(context, new T[0]);
                return;
            }

            var expTreeField = expression.GetType().GetField("expressionTree", BindingFlags.NonPublic | BindingFlags.Instance);

            var tree = expTreeField.GetValue(expression) as System.Linq.Expressions.Expression<System.Func<System.Activities.ActivityContext, bool>>;

            var visitor = new VBQueryFilterExpressionVisitor<T>();

            var linqExpression = visitor.Visit(tree) as System.Linq.Expressions.Expression<Func<T, bool>>;
            
            var filtered = Source.Get(context).Where(new RuntimeArgumentVisitor(runtimeArguments, context).Visit(linqExpression) as System.Linq.Expressions.Expression<Func<T, bool>>);

            Result.Set(context, filtered.ToArray());
        }

        /// <summary>
        /// Creates an instance of the activity class that represents a predefined design pattern for the specified target object.
        /// </summary>
        /// <returns>
        /// An instance of the activity class.
        /// </returns>
        /// <param name="target">The dependency object used by this instance of an <see cref="T:System.Activities.Presentation.IActivityTemplateFactory"/>.</param>
        public Activity Create(DependencyObject target) {
            return new Query<T> {
                DisplayName = "Query Something",
                Filter = new ActivityFunc<T, bool> {
                    Argument = new DelegateInArgument<T>("item")
                }
            };
        }

        internal static Collection<RuntimeArgument> BindArgumentsAndVariables(NativeActivityMetadata metadata, string expressionText, Collection<RuntimeArgument> runtimeArguments, string displayName, string id) {

            if (metadata.HasViolations) return runtimeArguments;

            runtimeArguments = metadata.GetArgumentsWithReflection();

            // bind the variables in scope
            LocationReferenceEnvironment env = metadata.Environment;

            while (env != null) { // traverse up the activity tree
                var variables = from variable in env.GetLocationReferences()
                                where variable is Variable
                                select variable as Variable;

                CreateArguments(metadata, variables, expressionText, runtimeArguments, displayName, id);
                env = env.Parent;
            }

            var root = metadata.Environment.Root as DynamicActivity;
            if (root != null) {
                CreateArguments(metadata, from property in root.Properties
                                          where property.Value is Argument
                                          select property, expressionText, runtimeArguments, displayName, id);
            }

            // bind the arguments in scope            
            CreateArguments(metadata, from property in metadata.Environment.Root.GetType().GetProperties()
                                      where IsArgument(property)
                                      select property, expressionText, runtimeArguments, displayName, id);

            metadata.SetArgumentsCollection(runtimeArguments);
            return runtimeArguments;
        }

        /// <summary>
        /// Determines whether the specified property is an argument.
        /// </summary>
        private static bool IsArgument(PropertyInfo property) {
            return typeof(Argument).IsAssignableFrom(property.PropertyType) &&
                   property.PropertyType.IsGenericType &&
                   (property.PropertyType.GetGenericTypeDefinition() == typeof(InArgument<>) ||
                    property.PropertyType.GetGenericTypeDefinition() == typeof(OutArgument<>) ||
                    property.PropertyType.GetGenericTypeDefinition() == typeof(InOutArgument<>));
        }

        /// <summary>
        /// Create runtime arguments from DynamicActivityProperty collection.
        /// </summary>
        private static void CreateArguments(NativeActivityMetadata metadata, IEnumerable<DynamicActivityProperty> properties, string expressionText, Collection<RuntimeArgument> runtimeArguments, string displayName, string id) {
            foreach (var property in properties.Where(prop => expressionText.Contains(prop.Name))) {
                var argument = (Argument)property.Value;
                var reference = Argument.CreateReference(argument, property.Name);
                var runtimeArgument = new RuntimeArgument(property.Name, reference.ArgumentType, reference.Direction);
                BindAndAddArgument(metadata, reference, runtimeArgument, runtimeArguments, displayName, id);

            }
        }

        /// <summary>
        /// Create runtime arguments from PropertyInfo collection.
        /// </summary>
        private static void CreateArguments(NativeActivityMetadata metadata, IEnumerable<PropertyInfo> properties, string expressionText, Collection<RuntimeArgument> runtimeArguments, string displayName, string id) {

            foreach (var propertyInfo in properties.Where(prop => expressionText.Contains(prop.Name))) {
                var argument = Argument.CreateReference((Argument)propertyInfo.GetValue(metadata.Environment.Root, null), propertyInfo.Name);
                var runtimeArgument = new RuntimeArgument(propertyInfo.Name, propertyInfo.PropertyType.GetGenericArguments()[0], argument.Direction);
                BindAndAddArgument(metadata, argument, runtimeArgument, runtimeArguments, displayName, id);
            }
        }

        /// <summary>
        /// Create runtime arguments from Variable collection
        /// </summary>
        private static void CreateArguments(NativeActivityMetadata metadata, IEnumerable<Variable> variables, string expressionText, Collection<RuntimeArgument> runtimeArguments, string displayName, string id) {

            foreach (Variable variable in variables.Where(variable => expressionText.Contains(variable.Name))) {

                Argument argument;
                RuntimeArgument runtimeArgument;

                if (variable.Modifiers == VariableModifiers.ReadOnly) {

                    runtimeArgument = new RuntimeArgument(variable.Name, variable.Type, ArgumentDirection.In, false);

                    //create an argument using reflection e.x. :  InArgument<string> argument = new InOutArgument<string>(variable); 
                    argument = (Argument)Activator.CreateInstance(typeof(InArgument<>).MakeGenericType(new Type[] { variable.Type }), variable);

                }
                else {

                    runtimeArgument = new RuntimeArgument(variable.Name, variable.Type, ArgumentDirection.InOut, false);

                    //for example : VariableReference<string> variableReference = new VariableReference<string>(variable);                                                           
                    object variableReference = Activator.CreateInstance(typeof(VariableReference<>).MakeGenericType(new Type[] { variable.Type }), variable);

                    //create an argument using reflection e.x. :  InOutArgument<string> argument = new InOutArgument<string>(variableReference); 
                    argument = (Argument)Activator.CreateInstance(typeof(InOutArgument<>).MakeGenericType(new Type[] { variable.Type }), variableReference);

                }

                BindAndAddArgument(metadata, argument, runtimeArgument, runtimeArguments, displayName, id);
            }
        }

        /// <summary>
        /// Binds and adds the argument to the metadata of the activity.
        /// </summary>
        private static void BindAndAddArgument(NativeActivityMetadata metadata, Argument argument, RuntimeArgument runtimeArgument, Collection<RuntimeArgument> runtimeArguments, string displayName, string id) {

            if (runtimeArguments.Any(r => r.Name == runtimeArgument.Name)) return;

            Debug.WriteLine("The activity {0}:{1} is about to bind runtime argument {2}.", displayName, id, runtimeArgument.Name);

            runtimeArguments.Add(runtimeArgument);
            metadata.Bind(argument, runtimeArgument);
            metadata.AddArgument(runtimeArgument);
        }

        public class VBQueryFilterExpressionVisitor<TItem> : System.Linq.Expressions.ExpressionVisitor {
            private System.Linq.Expressions.ParameterExpression itemParameter = System.Linq.Expressions.Expression.Parameter(typeof(TItem), "item");
            private Dictionary<string, System.Linq.Expressions.ParameterExpression> parameters = new Dictionary<string, System.Linq.Expressions.ParameterExpression>();
            public Dictionary<string, System.Linq.Expressions.ParameterExpression> Parameters {
                get { return parameters; }
            }

            public override System.Linq.Expressions.Expression Visit(System.Linq.Expressions.Expression node) {
                if (node is System.Linq.Expressions.Expression<System.Func<System.Activities.ActivityContext, bool>>) {
                    return Visit(node as System.Linq.Expressions.Expression<System.Func<System.Activities.ActivityContext, bool>>);
                }
                return base.Visit(node);
            }

            public System.Linq.Expressions.Expression Visit(System.Linq.Expressions.Expression<System.Func<System.Activities.ActivityContext, bool>> node){
                return Visit(LinqExpression.Lambda<Func<TItem, bool>>(node.Body, itemParameter));
            }

            protected override LinqExpression VisitMethodCall(System.Linq.Expressions.MethodCallExpression node) {
                if (node.Method.Name == "GetValue" && node.Object.Type == typeof(ActivityContext)) {
                    if (node.Arguments.Any() && node.Arguments.First() is System.Linq.Expressions.ConstantExpression && typeof(LocationReference).IsAssignableFrom(node.Arguments.First().Type)) {
                        if ((((System.Linq.Expressions.ConstantExpression)node.Arguments.First()).Value as LocationReference).Name == "item") {
                            // This is the item parameter for our Where Predicate
                            return itemParameter;
                        }
                        else { 
                            // We have a reference to another type of value. Extract the name and return a parameter expression
                            string dataItemName = (((System.Linq.Expressions.ConstantExpression)node.Arguments.First()).Value as LocationReference).Name;
                            if (parameters.ContainsKey(dataItemName)) {
                                return parameters[dataItemName];
                            }
                            else {
                                System.Linq.Expressions.ParameterExpression paramExpression = LinqExpression.Parameter((((System.Linq.Expressions.ConstantExpression)node.Arguments.First()).Value as LocationReference).Type, dataItemName);
                                parameters.Add(paramExpression.Name, paramExpression);
                                return paramExpression;
                            }
                        }
                    }
                }
                return base.VisitMethodCall(node);
            }
        }

        public class RuntimeArgumentVisitor : System.Linq.Expressions.ExpressionVisitor {

            private readonly Collection<RuntimeArgument> runtimeArguments;
            private readonly ActivityContext context;

            public RuntimeArgumentVisitor(Collection<RuntimeArgument> runtimeArguments, ActivityContext context) : base() {
                this.runtimeArguments = runtimeArguments;
                this.context = context;
            }

            protected override LinqExpression VisitParameter(System.Linq.Expressions.ParameterExpression node) {
                if (node.Name != "item") {
                    object value = runtimeArguments.Where(arg => arg.Name == node.Name).Single().Get(context);
                    return LinqExpression.Constant(value, node.Type);
                }
                return base.VisitParameter(node);
            }
        }
    }
}
