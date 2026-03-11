(() => {
  function isStore(candidate) {
    return Boolean(
      candidate &&
        candidate.graph &&
        candidate.state &&
        typeof candidate.requestRender === 'function' &&
        typeof candidate.openFigFile === 'function'
    )
  }

  function getVueAppRoots() {
    return Array.from(document.querySelectorAll('[data-v-app]'))
      .map((element) => element.__vue_app__)
      .filter(Boolean)
  }

  function resolveStoreFromVue() {
    const pending = []
    const seen = new Set()

    for (const app of getVueAppRoots()) {
      if (app && app._instance) {
        pending.push(app._instance)
      }
    }

    while (pending.length > 0) {
      const current = pending.shift()
      if (!current || seen.has(current)) {
        continue
      }

      seen.add(current)

      const setupStore = current.setupState?.store
      if (isStore(setupStore)) {
        return setupStore
      }

      const proxyStore = current.proxy?.store
      if (isStore(proxyStore)) {
        return proxyStore
      }

      if (current.subTree?.component) {
        pending.push(current.subTree.component)
      }

      if (Array.isArray(current.subTree?.children)) {
        for (const child of current.subTree.children) {
          if (child?.component) {
            pending.push(child.component)
          }
        }
      }
    }

    return null
  }

  function resolveStore() {
    if (isStore(window.__OPEN_PENCIL_STORE__)) {
      return window.__OPEN_PENCIL_STORE__
    }

    const fallbackStore = resolveStoreFromVue()
    if (isStore(fallbackStore)) {
      window.__OPEN_PENCIL_STORE__ = fallbackStore
      return fallbackStore
    }

    throw new Error('OpenPencil store is not available on window.__OPEN_PENCIL_STORE__.')
  }

  function ensureSelectionSet() {
    const store = resolveStore()
    if (store.state.selectedIds instanceof Set) {
      return Array.from(store.state.selectedIds)
    }

    const nextSelection = Array.isArray(store.state.selectedIds)
      ? store.state.selectedIds
      : []

    store.state.selectedIds = new Set(nextSelection)
    store.requestRender()
    return nextSelection
  }

  function getSceneSummary() {
    const store = resolveStore()
    const currentPage = store.graph.getNode(store.state.currentPageId)
    const topLevelNodes = (currentPage?.childIds ?? [])
      .map((nodeId) => store.graph.getNode(nodeId))
      .filter(Boolean)
      .map((node) => ({ id: node.id, name: node.name, type: node.type }))

    return {
      documentName: store.state.documentName,
      pageId: store.state.currentPageId,
      topLevelNodes,
      isUntitled: store.state.documentName === 'Untitled',
      pageName: currentPage?.name ?? null
    }
  }

  function hasTopLevelNames(expectedNames) {
    const normalizedExpectedNames = Array.isArray(expectedNames)
      ? expectedNames.filter((name) => typeof name === 'string' && name.trim().length > 0)
      : []

    if (normalizedExpectedNames.length === 0) {
      return true
    }

    const sceneSummary = getSceneSummary()
    const actualNames = new Set(sceneSummary.topLevelNodes.map((node) => node.name))
    return normalizedExpectedNames.every((name) => actualNames.has(name))
  }

  function buildOpenUrl(baseUrl, fileName) {
    const url = new URL(baseUrl, window.location.origin)
    url.searchParams.set('open', `/${fileName}`)
    url.searchParams.set('fit', '1')
    return url.toString()
  }

  window.__NEBULA_OPENPENCIL_AUTOMATION__ = {
    resolveStore,
    ensureSelectionSet,
    getSceneSummary,
    hasTopLevelNames,
    buildOpenUrl
  }
})()