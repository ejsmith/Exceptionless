﻿using System;
using System.Threading.Tasks;
using Exceptionless.Core.Extensions;
using Exceptionless.Core.Queries.Validation;
using Exceptionless.Core.Repositories.Configuration;
using Exceptionless.Core.Repositories.Queries;
using Foundatio.Logging;
using Foundatio.Parsers.ElasticQueries;
using Foundatio.Parsers.ElasticQueries.Visitors;
using Foundatio.Parsers.LuceneQueries.Nodes;
using Foundatio.Parsers.LuceneQueries.Visitors;
using Xunit;
using Xunit.Abstractions;

namespace Exceptionless.Api.Tests.Search {
    public class PersistentEventQueryValidatorTests : TestBase {
        private readonly ElasticQueryParser _parser;
        private readonly PersistentEventQueryValidator _validator;

        public PersistentEventQueryValidatorTests(ITestOutputHelper output) : base(output) {
            _parser = GetService<ExceptionlessElasticConfiguration>().Events.Event.QueryParser;
            _validator = GetService<PersistentEventQueryValidator>();
        }

        [Theory]
        [InlineData("data.@user.identity:blake", "data.@user.identity:blake", true, true)]
        [InlineData("data.user.identity:blake", "data.user.identity:blake", true, true)]
        [InlineData("_missing_:data.sessionend", "_missing_:data.sessionend", true, true)]
        [InlineData("data.sessionend:true", "data.sessionend:true", true, true)]
        [InlineData("data.SessionEnd:true", "data.SessionEnd:true", true, true)]
        [InlineData("data.haserror:true", "data.haserror:true", true, true)]
        [InlineData("data.field:(now criteria2)", "idx.field-s:(now criteria2)", true, true)]
        [InlineData("data.date:>now", "idx.date-d:>now", true, true)]
        [InlineData("data.date:[now/d-4d TO now/d+1d}", "idx.date-d:[now/d-4d TO now/d+1d}", true, true)]
        [InlineData("data.date:[2012-01-01 TO 2012-12-31]", "idx.date-d:[2012-01-01 TO 2012-12-31]", true, true)]
        [InlineData("data.date:[* TO 2012-12-31]", "idx.date-d:[* TO 2012-12-31]", true, true)]
        [InlineData("data.date:[2012-01-01 TO *]", "idx.date-d:[2012-01-01 TO *]", true, true)]
        [InlineData("(data.date:[now/d-4d TO now/d+1d})", "(idx.date-d:[now/d-4d TO now/d+1d})", true, true)]
        [InlineData("data.count:[1..5}", "idx.count-n:[1..5}", true, true)]
        [InlineData("data.Windows-identity:ejsmith", "idx.windows-identity-s:ejsmith", true, true)]
        [InlineData("data.age:(>30 AND <=40)", "idx.age-n:(>30 AND <=40)", true, true)]
        [InlineData("data.age:(+>=10 AND < 20)", "idx.age-n:(+>=10 AND <20)", true, true)]
        [InlineData("data.age:(+>=10 +<20)", "idx.age-n:(+>=10 +<20)", true, true)]
        [InlineData("data.age:(->=10 AND < 20)", "idx.age-n:(->=10 AND <20)", true, true)]
        [InlineData("data.age:[10 TO *]", "idx.age-n:[10 TO *]", true, true)]
        [InlineData("data.age:[* TO 10]", "idx.age-n:[* TO 10]", true, true)]
        [InlineData("hidden:true AND data.age:(>30 AND <=40)", "is_hidden:true AND idx.age-n:(>30 AND <=40)", true, true)]
        [InlineData("hidden:true", "is_hidden:true", true, false)]
        [InlineData("fixed:true", "is_fixed:true", true, false)]
        [InlineData("type:404", "type:404", true, false)]
        [InlineData("reference:404", "reference_id:404", true, false)]
        [InlineData("organization:404", "organization_id:404", true, false)]
        [InlineData("project:404", "project_id:404", true, false)]
        [InlineData("stack:404", "stack_id:404", true, false)]
        [InlineData("ref.session:12345678", "idx.session-r:12345678", true, true)]
        public async Task CanProcessQueryAsync(string query, string expected, bool isValid, bool usesPremiumFeatures) {
            var context = new ElasticQueryVisitorContext();

            IQueryNode result;
            try {
                result = await _parser.ParseAsync(query, QueryType.Query, context).AnyContext();
            } catch (Exception ex) {
                _logger.Error(ex, $"Error parsing query: {query}. Message: {ex.Message}");
                if (isValid)
                    throw;

                return;
            }

            // NOTE: we have to do this because we don't have access to the right query parser instance.
            result = await EventFieldsQueryVisitor.RunAsync(result, context);
            Assert.Equal(expected, await GenerateQueryVisitor.RunAsync(result, context));

            var info = await _validator.ValidateQueryAsync(result);
            _logger.Info(() => $"UsesPremiumFeatures: {info.UsesPremiumFeatures} IsValid: {info.IsValid} Message: {info.Message}");
            Assert.Equal(isValid, info.IsValid);
            Assert.Equal(usesPremiumFeatures, info.UsesPremiumFeatures);
        }

        [Theory]
        [InlineData(null, true, false)]
        [InlineData("avg", false, false)]
        [InlineData("avg:", false, false)]
        [InlineData("avg:val", false, true)]
        [InlineData("avg:value", true, false)]
        [InlineData("date:(date cardinality:stack_id) cardinality:stack_id terms:(is_first_occurrence @include:true)", true, false)] // dashboards
        [InlineData("date:(date cardinality:user sum:value avg:value) min:date max:date cardinality:user", true, false)] // stack dashboard
        [InlineData("avg:value cardinality:user date:(date cardinality:user)", true, false)] // session dashboard
        public async Task CanProcessAggregationsAsync(string query, bool isValid, bool usesPremiumFeatures) {
            var info = await _validator.ValidateAggregationsAsync(query);
            _logger.Info(() => $"UsesPremiumFeatures: {info.UsesPremiumFeatures} IsValid: {info.IsValid} Message: {info.Message}");
            Assert.Equal(isValid, info.IsValid);
            if (isValid)
                Assert.Equal(usesPremiumFeatures, info.UsesPremiumFeatures);
        }
    }
}