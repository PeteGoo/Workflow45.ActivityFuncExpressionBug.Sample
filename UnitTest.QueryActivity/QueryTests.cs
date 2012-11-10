using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Workflow40QueryActivity;
using System.Activities;
using Microsoft.VisualBasic.Activities;
using System.Activities.Statements;

namespace UnitTest.QueryActivity {
    [TestClass]
    public class QueryTests {
        [TestMethod]
        public void SimpleLinqQuery_FiltersCollection() {
            IEnumerable<StubData> sourceData = new StubData[]{
                new StubData{
                    FirstName = "Peter",
                    LastName = "Goodman"
                },
                new StubData{
                    FirstName = "Peter",
                    LastName = "Rabbit"
                },
                new StubData{
                    FirstName = "Stefan",
                    LastName = "Sewell"
                }
            };

            Query<StubData> activity = new Query<StubData>();

            ActivityFunc<StubData, bool> filterFunc = new ActivityFunc<StubData, bool>();
            DelegateInArgument<StubData> stubDataDelegateInArg = new DelegateInArgument<StubData>();
            filterFunc.Argument = stubDataDelegateInArg;
            stubDataDelegateInArg.Name = "item";

            filterFunc.Handler = new VisualBasicValue<bool>("item.FirstName = \"Peter\"");
            activity.Filter = filterFunc;

            VisualBasic.SetSettings(activity, GetVBSettings());

            var results = WorkflowInvoker.Invoke<IEnumerable<StubData>>(activity, new Dictionary<string, object>() { 
                {"Source", sourceData.AsQueryable()}
            });

            Assert.AreEqual(2, results.Count());
        }

        [TestMethod]
        public void LinqReferencingLocalVariable_FiltersCollection() {
            IEnumerable<StubData> sourceData = new StubData[]{
                new StubData{
                    FirstName = "Peter",
                    LastName = "Goodman"
                },
                new StubData{
                    FirstName = "Peter",
                    LastName = "Rabbit"
                },
                new StubData{
                    FirstName = "Stefan",
                    LastName = "Sewell"
                }
            };

            //Variable<IQueryable<StubData>> sourceDataVariable = new Variable<IQueryable<StubData>>("sourceData", sourceData.AsQueryable());
            Variable<IEnumerable<StubData>> resultDataVariable = new Variable<IEnumerable<StubData>>("resultData");

            Variable<string> expectedFirstNameVariable = new Variable<string>("expectedFirstName", "Peter");

            Query<StubData> queryActivity = new Query<StubData> { 
                Source = new InArgument<IQueryable<StubData>>(new System.Activities.Expressions.LambdaValue<IQueryable<StubData>>(context => sourceData.AsQueryable())),
                Result = new OutArgument<IEnumerable<StubData>>(resultDataVariable)
            };

            Sequence activity = new Sequence {
                Variables = {
                    //sourceDataVariable,
                    resultDataVariable,
                    expectedFirstNameVariable
                },
                Activities = {
                    queryActivity,
                    new AssertAreEqual<int>{
                        Expected = new InArgument<int>(2),
                        Actual = new InArgument<int>(new System.Activities.Expressions.LambdaValue<int>(context => resultDataVariable.Get(context).Count()))
                    }
                }
            };

            ActivityFunc<StubData, bool> filterFunc = new ActivityFunc<StubData, bool>();
            DelegateInArgument<StubData> stubDataDelegateInArg = new DelegateInArgument<StubData>();
            filterFunc.Argument = stubDataDelegateInArg;
            stubDataDelegateInArg.Name = "item";

            filterFunc.Handler = new VisualBasicValue<bool>("item.FirstName = expectedFirstName");
            queryActivity.Filter = filterFunc;

            VisualBasic.SetSettings(activity, GetVBSettings());

            var results = WorkflowInvoker.Invoke(activity);

            
        }

        

        private VisualBasicSettings GetVBSettings() {
            VisualBasicSettings vbSettings = new VisualBasicSettings();
            vbSettings.ImportReferences.Add(
                new VisualBasicImportReference { 
                    Assembly = "UnitTest.QueryActivity",
                    Import = "UnitTest.QueryActivity"
                }
            );

            return vbSettings;
        }


        public class StubData {
            public string FirstName { get; set; }
            public string LastName { get; set; }
        }
    }

    public class AssertAreEqual<T> : CodeActivity {
        [RequiredArgument]
        public InArgument<T> Expected { get; set; }

        [RequiredArgument]
        public InArgument<T> Actual { get; set; }

        protected override void Execute(CodeActivityContext context) {
            Assert.AreEqual<T>(Expected.Get(context), Actual.Get(context));
        }
    }
}
