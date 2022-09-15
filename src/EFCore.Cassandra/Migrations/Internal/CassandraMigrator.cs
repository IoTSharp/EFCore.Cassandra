﻿// Copyright (c) SimpleIdServer. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore.Cassandra.Migrations.Internal
{
    public class CassandraMigrator : IMigrator
    {
        private readonly IMigrationsAssembly _migrationsAssembly;
        private readonly ICassandraHistoryRepository _historyRepository;
        private readonly IRelationalDatabaseCreator _databaseCreator;
        private readonly IMigrationsSqlGenerator _migrationsSqlGenerator;
        private readonly IRawSqlCommandBuilder _rawSqlCommandBuilder;
        private readonly IMigrationCommandExecutor _migrationCommandExecutor;
        private readonly IRelationalConnection _connection;
        private readonly ISqlGenerationHelper _sqlGenerationHelper;
        private readonly IConventionSetBuilder _conventionSetBuilder;
        private readonly IDiagnosticsLogger<DbLoggerCategory.Migrations> _logger;
        private readonly IDiagnosticsLogger<DbLoggerCategory.Database.Command> _commandLogger;
        private readonly ICurrentDbContext _currentContext;
        private readonly string _activeProvider;

        public CassandraMigrator(
            IMigrationsAssembly migrationsAssembly,
            ICassandraHistoryRepository historyRepository,
            IDatabaseCreator databaseCreator,
            IMigrationsSqlGenerator migrationsSqlGenerator,
            IRawSqlCommandBuilder rawSqlCommandBuilder,
            IMigrationCommandExecutor migrationCommandExecutor,
            IRelationalConnection connection,
            ISqlGenerationHelper sqlGenerationHelper,
            ICurrentDbContext currentContext,
            IConventionSetBuilder conventionSetBuilder,
            IDiagnosticsLogger<DbLoggerCategory.Migrations> logger,
            IDiagnosticsLogger<DbLoggerCategory.Database.Command> commandLogger,
            IDatabaseProvider databaseProvider)
        {
            _migrationsAssembly = migrationsAssembly;
            _historyRepository = historyRepository;
            _databaseCreator = (IRelationalDatabaseCreator)databaseCreator;
            _migrationsSqlGenerator = migrationsSqlGenerator;
            _rawSqlCommandBuilder = rawSqlCommandBuilder;
            _migrationCommandExecutor = migrationCommandExecutor;
            _connection = connection;
            _sqlGenerationHelper = sqlGenerationHelper;
            _currentContext = currentContext;
            _conventionSetBuilder = conventionSetBuilder;
            _logger = logger;
            _commandLogger = commandLogger;
            _activeProvider = databaseProvider.Name;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual void Migrate(string targetMigration = null)
        {
            _logger.MigrateUsingConnection(this, _connection);

            if (!_historyRepository.Exists())
            {
                if (!_databaseCreator.Exists())
                {
                    _databaseCreator.Create();
                }

                var scripts = _historyRepository.GetCreateScripts();
                foreach (var script in scripts)
                {
                    var command = _rawSqlCommandBuilder.Build(script);
                    command.ExecuteNonQuery(
                    new RelationalCommandParameterObject(
                        _connection,
                        null,
                        null,
                        _currentContext.Context,
                        _commandLogger));
                }
            }

            var commandLists = GetMigrationCommandLists(_historyRepository.GetAppliedMigrations(), targetMigration);
            foreach (var commandList in commandLists)
            {
                _migrationCommandExecutor.ExecuteNonQuery(commandList(), _connection);
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual async Task MigrateAsync(
            string targetMigration = null,
            CancellationToken cancellationToken = default)
        {
            _logger.MigrateUsingConnection(this, _connection);

            if (!await _historyRepository.ExistsAsync(cancellationToken))
            {
                if (!await _databaseCreator.ExistsAsync(cancellationToken))
                {
                    await _databaseCreator.CreateAsync(cancellationToken);
                }

                var scripts = _historyRepository.GetCreateScripts();
                foreach(var script in scripts)
                {
                    var command = _rawSqlCommandBuilder.Build(script);
                    await command.ExecuteNonQueryAsync(
                    new RelationalCommandParameterObject(
                        _connection,
                        null,
                        null,
                        _currentContext.Context,
                        _commandLogger), cancellationToken: cancellationToken);
                }
            }

            var commandLists = GetMigrationCommandLists(
                await _historyRepository.GetAppliedMigrationsAsync(cancellationToken),
                targetMigration);

            foreach (var commandList in commandLists)
            {
                await _migrationCommandExecutor.ExecuteNonQueryAsync(commandList(), _connection, cancellationToken);
            }
        }

        private IEnumerable<Func<IReadOnlyList<MigrationCommand>>> GetMigrationCommandLists(
            IReadOnlyList<HistoryRow> appliedMigrationEntries,
            string targetMigration = null)
        {
            PopulateMigrations(
                appliedMigrationEntries.Select(t => t.MigrationId),
                targetMigration,
                out var migrationsToApply,
                out var migrationsToRevert,
                out var actualTargetMigration);

            for (var i = 0; i < migrationsToRevert.Count; i++)
            {
                var migration = migrationsToRevert[i];

                var index = i;
                yield return () =>
                {
                    _logger.MigrationReverting(this, migration);

                    return GenerateDownSql(
                        migration,
                        index != migrationsToRevert.Count - 1
                            ? migrationsToRevert[index + 1]
                            : actualTargetMigration);
                };
            }

            foreach (var migration in migrationsToApply)
            {
                yield return () =>
                {
                    _logger.MigrationApplying(this, migration);

                    return GenerateUpSql(migration);
                };
            }

            if (migrationsToRevert.Count + migrationsToApply.Count == 0)
            {
                _logger.MigrationsNotApplied(this);
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual void PopulateMigrations(
            IEnumerable<string> appliedMigrationEntries,
            string targetMigration,
            out IReadOnlyList<Migration> migrationsToApply,
            out IReadOnlyList<Migration> migrationsToRevert,
            out Migration actualTargetMigration)
        {
            var appliedMigrations = new Dictionary<string, TypeInfo>();
            var unappliedMigrations = new Dictionary<string, TypeInfo>();
            var appliedMigrationEntrySet = new HashSet<string>(appliedMigrationEntries, StringComparer.OrdinalIgnoreCase);
            if (_migrationsAssembly.Migrations.Count == 0)
            {
                _logger.MigrationsNotFound(this, _migrationsAssembly);
            }

            foreach (var migration in _migrationsAssembly.Migrations)
            {
                if (appliedMigrationEntrySet.Contains(migration.Key))
                {
                    appliedMigrations.Add(migration.Key, migration.Value);
                }
                else
                {
                    unappliedMigrations.Add(migration.Key, migration.Value);
                }
            }

            if (string.IsNullOrEmpty(targetMigration))
            {
                migrationsToApply = unappliedMigrations
                    .Select(p => _migrationsAssembly.CreateMigration(p.Value, _activeProvider))
                    .ToList();
                migrationsToRevert = Array.Empty<Migration>();
                actualTargetMigration = null;
            }
            else if (targetMigration == Migration.InitialDatabase)
            {
                migrationsToApply = Array.Empty<Migration>();
                migrationsToRevert = appliedMigrations
                    .OrderByDescending(m => m.Key)
                    .Select(p => _migrationsAssembly.CreateMigration(p.Value, _activeProvider))
                    .ToList();
                actualTargetMigration = null;
            }
            else
            {
                targetMigration = _migrationsAssembly.GetMigrationId(targetMigration);
                migrationsToApply = unappliedMigrations
                    .Where(m => string.Compare(m.Key, targetMigration, StringComparison.OrdinalIgnoreCase) <= 0)
                    .Select(p => _migrationsAssembly.CreateMigration(p.Value, _activeProvider))
                    .ToList();
                migrationsToRevert = appliedMigrations
                    .Where(m => string.Compare(m.Key, targetMigration, StringComparison.OrdinalIgnoreCase) > 0)
                    .OrderByDescending(m => m.Key)
                    .Select(p => _migrationsAssembly.CreateMigration(p.Value, _activeProvider))
                    .ToList();
                actualTargetMigration = appliedMigrations
                    .Where(m => string.Compare(m.Key, targetMigration, StringComparison.OrdinalIgnoreCase) == 0)
                    .Select(p => _migrationsAssembly.CreateMigration(p.Value, _activeProvider))
                    .SingleOrDefault();
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public string GenerateScript(string fromMigration = null, string toMigration = null, MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
        {
            var idempotent = options == MigrationsSqlGenerationOptions.Idempotent;
            IEnumerable<string> appliedMigrations;
            if (string.IsNullOrEmpty(fromMigration)
                || fromMigration == Migration.InitialDatabase)
            {
                appliedMigrations = Enumerable.Empty<string>();
            }
            else
            {
                var fromMigrationId = _migrationsAssembly.GetMigrationId(fromMigration);
                appliedMigrations = _migrationsAssembly.Migrations
                    .Where(t => string.Compare(t.Key, fromMigrationId, StringComparison.OrdinalIgnoreCase) <= 0)
                    .Select(t => t.Key);
            }

            PopulateMigrations(
                appliedMigrations,
                toMigration,
                out var migrationsToApply,
                out var migrationsToRevert,
                out var actualTargetMigration);

            var builder = new IndentedStringBuilder();

            if (fromMigration == Migration.InitialDatabase
                || string.IsNullOrEmpty(fromMigration))
            {
                builder.AppendLine(_historyRepository.GetCreateIfNotExistsScript());
                builder.Append(_sqlGenerationHelper.BatchTerminator);
            }

            for (var i = 0; i < migrationsToRevert.Count; i++)
            {
                var migration = migrationsToRevert[i];
                var previousMigration = i != migrationsToRevert.Count - 1
                    ? migrationsToRevert[i + 1]
                    : actualTargetMigration;
                _logger.MigrationGeneratingDownScript(this, migration, fromMigration, toMigration, idempotent);

                foreach (var command in GenerateDownSql(migration, previousMigration))
                {
                    if (idempotent)
                    {
                        builder.AppendLine(_historyRepository.GetBeginIfExistsScript(migration.GetId()));
                        using (builder.Indent())
                        {
                            builder.AppendLines(command.CommandText);
                        }

                        builder.AppendLine(_historyRepository.GetEndIfScript());
                    }
                    else
                    {
                        builder.AppendLine(command.CommandText);
                    }

                    builder.Append(_sqlGenerationHelper.BatchTerminator);
                }
            }

            foreach (var migration in migrationsToApply)
            {
                _logger.MigrationGeneratingUpScript(this, migration, fromMigration, toMigration, idempotent);

                foreach (var command in GenerateUpSql(migration))
                {
                    if (idempotent)
                    {
                        builder.AppendLine(_historyRepository.GetBeginIfNotExistsScript(migration.GetId()));
                        using (builder.Indent())
                        {
                            builder.AppendLines(command.CommandText);
                        }

                        builder.AppendLine(_historyRepository.GetEndIfScript());
                    }
                    else
                    {
                        builder.AppendLine(command.CommandText);
                    }

                    builder.Append(_sqlGenerationHelper.BatchTerminator);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual IReadOnlyList<MigrationCommand> GenerateUpSql(
            Migration migration,
            MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
        {
            var insertCommand = _rawSqlCommandBuilder.Build(
                _historyRepository.GetInsertScript(new HistoryRow(migration.GetId(), ProductInfo.GetVersion())));

            return _migrationsSqlGenerator
                .Generate(migration.UpOperations, FinalizeModel(migration.TargetModel), options)
                .Concat(new[] { new MigrationCommand(insertCommand, _currentContext.Context, _commandLogger) })
                .ToList();
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        protected virtual IReadOnlyList<MigrationCommand> GenerateDownSql(
            [NotNull] Migration migration,
            Migration previousMigration,
            MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
        {
            var deleteCommand = _rawSqlCommandBuilder.Build(
                _historyRepository.GetDeleteScript(migration.GetId()));

            return _migrationsSqlGenerator
                .Generate(migration.DownOperations, FinalizeModel(previousMigration?.TargetModel), options)
                .Concat(new[] { new MigrationCommand(deleteCommand, _currentContext.Context, _commandLogger) })
                .ToList();
        }

        private IModel FinalizeModel(IModel model)
        {
            if (model is IConventionModel conventionModel)
            {
                var conventionSet = _conventionSetBuilder.CreateConventionSet();

                var typeMappingConvention = conventionSet.ModelFinalizingConventions.OfType<TypeMappingConvention>().FirstOrDefault();
                if (typeMappingConvention != null)
                {
                    typeMappingConvention.ProcessModelFinalizing(conventionModel.Builder, null);
                }

                var relationalModelConvention =
                    conventionSet.ModelFinalizedConventions.OfType<RelationalModelConvention>().FirstOrDefault();
                if (relationalModelConvention != null)
                {
                    relationalModelConvention.ProcessModelFinalized(conventionModel);
                }

                return conventionModel.FinalizeModel();
            }

            return model;
        }
    }
}
