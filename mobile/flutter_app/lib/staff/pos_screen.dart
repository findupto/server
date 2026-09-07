import 'dart:async';
import 'package:flutter/material.dart';
import '../core/api_client.dart';
import 'offline_sale_queue.dart';

class PosScreen extends StatefulWidget {
  const PosScreen({super.key, required this.api});
  final PosApiClient api;
  @override
  State<PosScreen> createState() => _PosScreenState();
}

class _PosScreenState extends State<PosScreen> with WidgetsBindingObserver {
  final _barcode = TextEditingController();
  final _search = TextEditingController();
  final _tenderedController = TextEditingController(text: '0');
  final _reference = TextEditingController();
  final _cart = <int, _CartLine>{};
  final _queue = OfflineSaleQueue();
  Timer? _retryTimer;

  List<Map<String, dynamic>> _products = [];
  List<Map<String, dynamic>> _tables = [];
  String _orderType = 'Dine In';
  String _payment = 'Cash';
  String _status = '';
  int? _tableId;
  int? _pendingCount;
  int? _lastOrderId;
  double _taxPercent = 0;
  String _currency = 'Rs.';
  bool _loading = true;
  bool _saving = false;
  bool _syncing = false;
  bool _printing = false;

  double get _tendered => double.tryParse(_tenderedController.text.trim()) ?? 0;
  double get _subtotal => _cart.values.fold(0, (sum, line) => sum + line.price * line.quantity);
  double get _tax => double.parse((_subtotal * _taxPercent / 100).toStringAsFixed(2));
  double get _total => _subtotal + _tax;
  double get _change => _payment == 'Cash' ? (_tendered - _total).clamp(0, double.infinity) : 0;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _loadCatalog();
    _refreshQueue();
    _retryTimer = Timer.periodic(const Duration(seconds: 30), (_) => _syncQueue(silent: true));
  }

  Future<void> _loadCatalog() async {
    try {
      final products = await widget.api.products();
      final tables = await widget.api.tables();
      final settings = await widget.api.settings();
      if (!mounted) return;
      setState(() {
        _products = products.whereType<Map<String, dynamic>>().where((x) => x['available'] == true).toList();
        _tables = tables.whereType<Map<String, dynamic>>().where((x) => x['active'] == true && x['status'] == 'Available').toList();
        _taxPercent = (settings['taxPercent'] as num?)?.toDouble() ?? 0;
        _currency = '${settings['currencySymbol'] ?? 'Rs.'}';
        _loading = false;
      });
    } catch (_) {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _refreshQueue() async {
    final count = await _queue.count();
    if (mounted) setState(() => _pendingCount = count);
  }

  Future<void> _syncQueue({bool silent = false}) async {
    if (_syncing) return;
    if (await _queue.count() == 0) {
      if (!silent && mounted) setState(() => _status = 'No pending offline sales.');
      return;
    }
    if (mounted) setState(() => _syncing = true);
    try {
      final result = await _queue.sync(widget.api);
      if (!mounted) return;
      setState(() => _pendingCount = result.pending);
      if (!silent) {
        setState(() => _status = 'Synced ${result.completed}; ${result.conflicts} conflict(s); ${result.pending} pending.');
      }
    } catch (_) {
      if (!silent && mounted) setState(() => _status = 'Sync unavailable; sales remain queued.');
    } finally {
      if (mounted) setState(() => _syncing = false);
    }
  }

  void _add(Map<String, dynamic> product) {
    final id = (product['id'] as num).toInt();
    final name = '${product['name'] ?? ''}';
    final price = (product['price'] as num).toDouble();
    setState(() {
      final line = _cart[id];
      _cart[id] = _CartLine(id, name, price, (line?.quantity ?? 0) + 1);
      _status = '$name added';
      if (_payment == 'Cash' && _tenderedController.text == '0') {
        _tenderedController.text = _total.toStringAsFixed(2);
      }
    });
  }

  void _scan() {
    final value = _barcode.text.trim();
    if (value.isEmpty) return;
    Map<String, dynamic>? match;
    for (final product in _products) {
      if ('${product['barcode'] ?? ''}' == value) {
        match = product;
        break;
      }
    }
    if (match == null) {
      setState(() => _status = 'Barcode not found: $value');
    } else {
      _add(match);
      _barcode.clear();
    }
  }

  Future<void> _printReceipt(int id) async {
    if (_printing) return;
    setState(() => _printing = true);
    try {
      await widget.api.printReceipt(id);
      if (mounted) setState(() => _status = 'Receipt for sale #$id printed.');
    } catch (_) {
      if (mounted) setState(() => _status = 'Sale #$id completed, but printer is unavailable. Retry printing when connected.');
    } finally {
      if (mounted) setState(() => _printing = false);
    }
  }

  Future<void> _completeSale() async {
    if (_cart.isEmpty || _saving) return;
    if (_orderType == 'Dine In' && _tableId == null) {
      setState(() => _status = 'Select a table for Dine In');
      return;
    }
    if (_payment == 'Cash' && _tendered < _total) {
      setState(() => _status = 'Insufficient cash. Need $_currency${(_total - _tendered).toStringAsFixed(2)} more.');
      return;
    }
    if ((_payment == 'Card' || _payment == 'Online') && _reference.text.trim().isEmpty) {
      setState(() => _status = 'Enter the payment reference for $_payment.');
      return;
    }

    setState(() => _saving = true);
    final operationId = 'pos-${DateTime.now().toUtc().microsecondsSinceEpoch}';
    final paymentOperationId = '$operationId:payment';
    final localChange = _change;
    final sale = <String, dynamic>{
      'clientOperationId': operationId,
      'paymentClientOperationId': paymentOperationId,
      'tableId': _orderType == 'Dine In' ? _tableId : null,
      'orderType': _orderType,
      'notes': '',
      'items': _cart.values.map((line) => {
        'productId': line.id,
        'quantity': line.quantity,
        'notes': '',
      }).toList(),
      'paymentMethod': _payment,
      'amountTendered': _payment == 'Cash' ? _tendered : _total,
      'paymentReference': _reference.text.trim(),
    };

    try {
      final order = await widget.api.createStaffOrder(
        tableId: sale['tableId'] as int?,
        orderType: _orderType,
        clientOperationId: operationId,
        items: List<Map<String, dynamic>>.from(sale['items'] as List),
      );
      final id = (order['id'] as num).toInt();
      final total = (order['total'] as num?)?.toDouble() ?? _total;
      final tendered = _payment == 'Cash' ? _tendered : total;
      await widget.api.collectPayment(
        id,
        amountTendered: tendered,
        method: _payment,
        reference: _reference.text.trim(),
        clientOperationId: paymentOperationId,
      );
      if (!mounted) return;
      setState(() {
        _lastOrderId = id;
        _cart.clear();
        _tableId = null;
        _tenderedController.text = '0';
        _reference.clear();
        _saving = false;
        _status = 'Sale #$id completed${_payment == 'Cash' && localChange > 0 ? ' — Change $_currency${localChange.toStringAsFixed(2)}' : ''}';
      });
      await _refreshQueue();
      await _loadCatalog();
      await _printReceipt(id);
    } catch (_) {
      await _queue.enqueue(sale);
      await _refreshQueue();
      if (!mounted) return;
      setState(() {
        _cart.clear();
        _tableId = null;
        _tenderedController.text = '0';
        _reference.clear();
        _saving = false;
        _status = 'Offline/recovery: sale queued. $_pendingCount pending.';
      });
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) _syncQueue(silent: true);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _retryTimer?.cancel();
    _barcode.dispose();
    _search.dispose();
    _tenderedController.dispose();
    _reference.dispose();
    _queue.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final query = _search.text.toLowerCase();
    final products = _products.where((product) =>
        '${product['name'] ?? ''}'.toLowerCase().contains(query) ||
        '${product['barcode'] ?? ''}'.contains(query));

    return Scaffold(
      appBar: AppBar(
        title: const Text('FindUpTo POS — Counter'),
        actions: [
          if ((_pendingCount ?? 0) > 0)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 8),
              child: Center(child: Text('$_pendingCount pending')),
            ),
          IconButton(onPressed: _syncing ? null : () => _syncQueue(), icon: const Icon(Icons.sync)),
          IconButton(onPressed: _loadCatalog, icon: const Icon(Icons.refresh)),
        ],
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : Row(
              children: [
                Expanded(
                  flex: 3,
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      children: [
                        TextField(
                          controller: _search,
                          onChanged: (_) => setState(() {}),
                          decoration: const InputDecoration(
                            prefixIcon: Icon(Icons.search),
                            labelText: 'Search products',
                            border: OutlineInputBorder(),
                          ),
                        ),
                        const SizedBox(height: 12),
                        TextField(
                          controller: _barcode,
                          onSubmitted: (_) => _scan(),
                          decoration: const InputDecoration(
                            prefixIcon: Icon(Icons.qr_code_scanner),
                            labelText: 'Scan / enter barcode',
                            border: OutlineInputBorder(),
                          ),
                        ),
                        const SizedBox(height: 16),
                        Expanded(
                          child: products.isEmpty
                              ? const Center(child: Text('No products found'))
                              : GridView.count(
                                  crossAxisCount: 3,
                                  childAspectRatio: 1.35,
                                  crossAxisSpacing: 10,
                                  mainAxisSpacing: 10,
                                  children: [
                                    for (final product in products)
                                      Card(
                                        child: InkWell(
                                          onTap: () => _add(product),
                                          child: Center(
                                            child: Column(
                                              mainAxisSize: MainAxisSize.min,
                                              children: [
                                                Text('${product['name']}', style: Theme.of(context).textTheme.titleMedium),
                                                const SizedBox(height: 6),
                                                Text('$_currency ${((product['price'] as num).toDouble()).toStringAsFixed(0)}'),
                                              ],
                                            ),
                                          ),
                                        ),
                                      ),
                                  ],
                                ),
                        ),
                      ],
                    ),
                  ),
                ),
                const VerticalDivider(width: 1),
                Expanded(
                  flex: 2,
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Text('Current Sale', style: Theme.of(context).textTheme.headlineSmall),
                        const SizedBox(height: 8),
                        Expanded(
                          child: _cart.isEmpty
                              ? const Center(child: Text('Scan or select products'))
                              : ListView(
                                  children: [
                                    for (final line in _cart.values)
                                      ListTile(
                                        title: Text(line.name),
                                        subtitle: Text('$_currency ${line.price.toStringAsFixed(0)} × ${line.quantity}'),
                                        trailing: Row(
                                          mainAxisSize: MainAxisSize.min,
                                          children: [
                                            IconButton(
                                              onPressed: () => setState(() {
                                                if (line.quantity > 1) {
                                                  line.quantity--;
                                                } else {
                                                  _cart.remove(line.id);
                                                }
                                              }),
                                              icon: const Icon(Icons.remove),
                                            ),
                                            IconButton(
                                              onPressed: () => _add(_products.firstWhere((p) => (p['id'] as num).toInt() == line.id)),
                                              icon: const Icon(Icons.add),
                                            ),
                                          ],
                                        ),
                                      ),
                                  ],
                                ),
                        ),
                        DropdownButtonFormField<String>(
                          initialValue: _orderType,
                          items: const ['Dine In', 'Takeaway', 'Delivery'].map((x) => DropdownMenuItem(value: x, child: Text(x))).toList(),
                          onChanged: (x) => setState(() {
                            _orderType = x!;
                            if (_orderType != 'Dine In') _tableId = null;
                          }),
                          decoration: const InputDecoration(labelText: 'Order type'),
                        ),
                        if (_orderType == 'Dine In')
                          DropdownButtonFormField<int>(
                            initialValue: _tableId,
                            items: _tables.map((table) => DropdownMenuItem(value: (table['id'] as num).toInt(), child: Text('${table['name']}'))).toList(),
                            onChanged: (x) => setState(() => _tableId = x),
                            decoration: const InputDecoration(labelText: 'Table'),
                          ),
                        DropdownButtonFormField<String>(
                          initialValue: _payment,
                          items: const ['Cash', 'Card', 'Online'].map((x) => DropdownMenuItem(value: x, child: Text(x))).toList(),
                          onChanged: (x) => setState(() {
                            _payment = x!;
                            if (_payment != 'Cash') _tenderedController.text = _total.toStringAsFixed(2);
                          }),
                          decoration: const InputDecoration(labelText: 'Payment'),
                        ),
                        if (_payment == 'Cash')
                          TextField(
                            controller: _tenderedController,
                            keyboardType: const TextInputType.numberWithOptions(decimal: true),
                            onChanged: (_) => setState(() {}),
                            decoration: InputDecoration(labelText: 'Cash tendered', prefixText: '$_currency '),
                          ),
                        if (_payment == 'Card' || _payment == 'Online')
                          TextField(
                            controller: _reference,
                            decoration: const InputDecoration(labelText: 'Payment reference', border: OutlineInputBorder()),
                          ),
                        const SizedBox(height: 12),
                        Text('Subtotal: $_currency ${_subtotal.toStringAsFixed(2)}'),
                        Text('Tax (${_taxPercent.toStringAsFixed(2)}%): $_currency ${_tax.toStringAsFixed(2)}'),
                        Text('TOTAL: $_currency ${_total.toStringAsFixed(2)}', style: Theme.of(context).textTheme.titleLarge),
                        if (_payment == 'Cash') Text('Change: $_currency ${_change.toStringAsFixed(2)}'),
                        const SizedBox(height: 12),
                        FilledButton.icon(
                          onPressed: _saving ? null : _completeSale,
                          icon: const Icon(Icons.point_of_sale),
                          label: Text(_saving ? 'Processing...' : 'Complete Sale'),
                        ),
                        if (_lastOrderId != null)
                          OutlinedButton.icon(
                            onPressed: _printing ? null : () => _printReceipt(_lastOrderId!),
                            icon: const Icon(Icons.print),
                            label: Text(_printing ? 'Printing...' : 'Reprint Receipt'),
                          ),
                        if (_status.isNotEmpty) Padding(padding: const EdgeInsets.only(top: 8), child: Text(_status)),
                      ],
                    ),
                  ),
                ),
              ],
            ),
    );
  }
}

class _CartLine {
  _CartLine(this.id, this.name, this.price, this.quantity);
  final int id;
  final String name;
  final double price;
  int quantity;
}
