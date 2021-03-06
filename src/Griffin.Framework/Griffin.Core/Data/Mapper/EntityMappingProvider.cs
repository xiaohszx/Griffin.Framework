﻿using System;
using System.Collections.Generic;

namespace Griffin.Data.Mapper
{
    /// <summary>
    ///     Facade for the current <see cref="IMappingProvider" /> implementation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This facade exists so that no code needs to be refactored if the mapping provider is replace with another
    ///         one.
    ///     </para>
    ///     <para>
    ///         The <see cref="AssemblyScanningMappingProvider" /> is used per default (if no other provider is assigned). It
    ///         is lazy loaded so that
    ///         all assemblies have a chance to be loaded before it scans all assemblies in the current appdomain.
    ///     </para>
    /// </remarks>
    public class EntityMappingProvider
    {
        private static IMappingProvider _provider = new AssemblyScanningMappingProvider();
        private static bool _scanned = false;
        private static Dictionary<Type, IEntityMapper> _mirrorMappers = new Dictionary<Type, IEntityMapper>();

        /// <summary>
        ///     Provider to use.
        /// </summary>
        /// <value>
        ///     Default is <see cref="AssemblyScanningMappingProvider" />. Read the class remarks for more information.
        /// </value>
        public static IMappingProvider Provider
        {
            get { return _provider; }
            set
            {
                if (value == null)
                    throw new ArgumentNullException("value");

                _provider = value;
            }
        }

        /// <summary>
        /// Add mappers which take for granted that table columns match class properties  (in names and count)
        /// </summary>
        public static bool UseAutoMappers { get; set; }

        /// <summary>
        ///     Get a mapper.
        /// </summary>
        /// <typeparam name="TEntity">Type of entity to get a mapper for.</typeparam>
        /// <exception cref="MappingNotFoundException">Did not find a mapper for the specified entity.</exception>
        /// <returns></returns>
        public static IEntityMapper<TEntity> GetEntityMapper<TEntity>()
        {
            EnsureThatAssembliesHaveBeenScanned();

            var mapper1 = _provider.GetBase<TEntity>();
            var mapper = mapper1 as IEntityMapper<TEntity>;
            if (mapper != null)
                return mapper;

            if (!UseAutoMappers)
            {
                throw new MappingException(typeof(TEntity),
                    "Expected to find a mapper for " +
                    typeof(TEntity).FullName);
            }

            IEntityMapper em;
            if (_mirrorMappers.TryGetValue(typeof(TEntity), out em) && em is IEntityMapper<TEntity>)
            {
                return (EntityMapper<TEntity>)em;
            }

            em = new MirrorMapper<TEntity>();
            _mirrorMappers[typeof(TEntity)] = em;
            return (IEntityMapper<TEntity>)em;
        }

        /// <summary>
        ///     Get a mapper.
        /// </summary>
        /// <typeparam name="TEntity">Type of entity to get a mapper for.</typeparam>
        /// <exception cref="MappingNotFoundException">Did not find a mapper for the specified entity.</exception>
        /// <returns></returns>
        public static ICrudEntityMapper<TEntity> GetCrudMapper<TEntity>()
        {
            EnsureThatAssembliesHaveBeenScanned();

            var mapperFound = _provider.Get<TEntity>();
            var mapper = mapperFound as ICrudEntityMapper<TEntity>;
            if (mapper == null)
                throw new MappingException(typeof (TEntity),
                    "Expected to find a ICrudEntityMapper but only found a IEntityMapper for the requested operation to work. Found mapper: " +
                    mapperFound.GetType().FullName);
            return mapper;
        }

        private static void EnsureThatAssembliesHaveBeenScanned()
        {
            if (!_scanned && _provider is AssemblyScanningMappingProvider)
            {
                var provider = (AssemblyScanningMappingProvider) _provider;
                _scanned = true;
                if (!provider.HasScanned)
                    provider.Scan();
            }
        }

        /// <summary>
        ///     Get a mapper.
        /// </summary>
        /// <typeparam name="TEntity">Type of entity to get a mapper for.</typeparam>
        /// <exception cref="MappingNotFoundException">Did not find a mapper for the specified entity.</exception>
        /// <returns></returns>
        public static IEntityMapper<TEntity> GetBaseMapper<TEntity>()
        {
            EnsureThatAssembliesHaveBeenScanned();

            var mapper = (IEntityMapper<TEntity>) _provider.GetBase<TEntity>();
            return mapper;
        }
    }
}