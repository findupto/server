import 'package:flutter/material.dart';
import '../core/api_client.dart';
import 'offline_sale_queue.dart';

class PosScreen extends StatefulWidget {
  const PosScreen({super.key, required this.api});
  final PosApiClient api;

  @override
  State<PosScreen> createState() => _PosScreenState();
}

class _PosScreenState extends State<PosScreen> {
  final _barcode = TextEditingController();
  final _search = TextEditingController();
  final _cart = <int, _CartLine>{};
  final _queue = OfflineSaleQueue();
  List<Map<String, dynamic>> _products = [];
  List<Map<String, dynamic>> _tables = [];
  String _orderType = 'Dine In';
  String _payment = 'Cash';
  String _status = '';
  int? _tableId;
  int _pendingCount = 0;
  bool _loading = true;
  bool _saving = false;
  bool _syncing = false;

  @override
  void initState() {
    super.initState();
    _loadCatalog();
    _refreshQueue();
  }

  Future<void> _loadCatalog() async {
    try {
      final products = await widget.api.products();
      final tables = await widget.api.tables();
      if (!mounted) return;
      setState(() {
        _products = products.whereType<Map<String, dynamic>>().where((p) => p['available'] == true).toList();
        _tables = tables.whereType<Map<String, dynamic>>().where((t) => t['active'] == true && t['status'] == 'Available').toList();
        _loading = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() { _loading = false; _status = 'Offline: catalog unavailable. Queued sales can still retry.'; });
    }
  }

  Future<void> _refreshQueue() async {
    final count = await _queue.count();
    if (mounted) setState(() => _pendingCount = count);
  }

  Future<void> _syncQueue() async {
    if (_syncing) return;
    setState(() { _syncing = true; _status = 'Syncing pending sales...'; });
    final result = await _queue.sync(widget.api);
    if (!mounted) return;
    setState(() {
      _syncing = false;
      _pendingCount = result.pending;
      _status = result.completed > 0 || result.conflicts > 0
          ? 'Synced ${result.completed} sale(s); ${result.conflicts} conflict(s); ${result.pending} still pending.'
          : (result.pending > 0 ? 'Still offline. ${result.pending} sale(s) pending.' : 'All offline sales synced.');
    });
  }

  void _add(Map<String, dynamic> p) {
    final id = (p['id'] as num).toInt();
    final name = '${p['name'] ?? ''}';
    final price = (p['price'] as num).toDouble();
    setState(() {
      final line = _cart[id];
      _cart[id] = _CartLine(id, name, price, (line?.quantity ?? 0) + 1);
      _status = '$name added';
    });
  }

  void _scan() {
    final code = _barcode.text.trim();
    if (code.isEmpty) return;
    final match = _products.where((p) => '${p['barcode'] ?? ''}' == code).firstOrNull;
    if (match == null) {
      setState(() => _status = 'Barcode not found: $code');
    } else {
      _add(match);
      _barcode.clear();
    }
  }

  double get _subtotal => _cart.values.fold(0, (s, x) => s + x.price * x.quantity);

  Future<void> _completeSale() async {
    if (_cart.isEmpty || _saving) return;
    if (_orderType == 'Dine In' && _tableId == null) {
      setState(() => _status = 'Select a table for Dine In');
      return;
    }
    setState(() { _saving = true; _status = 'Saving sale...'; });
    final opId = 'pos-${DateTime.now().toUtc().microsecondsSinceEpoch}';
    final sale = <String, dynamic>{
      'clientOperationId': opId,
      'tableId': _orderType == 'Dine In' ? _tableId : null,
      'orderType': _orderType,
      'notes': '',
      'items': _cart.values.map((x) => {'productId': x.id, 'quantity': x.quantity, 'notes': ''}).toList(),
    };
    try {
      final order = await widget.api.createStaffOrder(
        tableId: sale['tableId'] as int?, orderType: _orderType,
        clientOperationId: opId, items: List<Map<String, dynamic>>.from(sale['items'] as List),
      );
      final orderId = (order['id'] as num).toInt();
      final total = (order['total'] as num?)?.toDouble() ?? _subtotal;
      await widget.api.collectPayment(orderId, amountTendered: total, method: _payment);
      if (!mounted) return;
      setState(() { _cart.clear(); _tableId = null; _saving = false; _status = 'Sale #$orderId completed'; });
      await _refreshQueue();
      await _loadCatalog();
    } catch (e) {
      await _queue.enqueue(sale);
      await _refreshQueue();
      if (!mounted) return;
      setState(() { _cart.clear(); _tableId = null; _saving = false; _status = 'Offline: sale queued for automatic retry. $_pendingCount pending.'; });
    }
  }

  @override
  void dispose() {
    _barcode.dispose();
    _search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final query = _search.text.toLowerCase();
    final products = _products.where((p) => '${p['name'] ?? ''}'.toLowerCase().contains(query) || '${p['barcode'] ?? ''}'.contains(query));
    return Scaffold(
      appBar: AppBar(title: const Text('FindUpTo POS — Counter'), actions: [
        if (_pendingCount > 0) Padding(padding: const EdgeInsets.symmetric(horizontal: 8), child: Center(child: Text('$_pendingCount pending'))),
        IconButton(onPressed: _syncing ? null : _syncQueue, icon: const Icon(Icons.sync)),
        IconButton(onPressed: _loadCatalog, icon: const Icon(Icons.refresh)),
      ]),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : Row(children: [
              Expanded(flex: 3, child: Padding(padding: const EdgeInsets.all(16), child: Column(children: [
                TextField(controller: _search, onChanged: (_) => setState(() {}), decoration: const InputDecoration(prefixIcon: Icon(Icons.search), labelText: 'Search products', border: OutlineInputBorder())),
                const SizedBox(height: 12),
                TextField(controller: _barcode, onSubmitted: (_) => _scan(), decoration: const InputDecoration(prefixIcon: Icon(Icons.qr_code_scanner), labelText: 'Scan / enter barcode', border: OutlineInputBorder())),
                const SizedBox(height: 16),
                Expanded(child: products.isEmpty ? const Center(child: Text('No products found')) : GridView.count(crossAxisCount: 3, childAspectRatio: 1.35, crossAxisSpacing: 10, mainAxisSpacing: 10, children: [for (final p in products) Card(child: InkWell(onTap: () => _add(p), child: Center(child: Column(mainAxisSize: MainAxisSize.min, children: [Text('${p['name']}', style: Theme.of(context).textTheme.titleMedium), const SizedBox(height: 6), Text('Rs. ${((p['price'] as num).toDouble()).toStringAsFixed(0)}')]))))]))),
              ]))),
              const VerticalDivider(width: 1),
              Expanded(flex: 2, child: Padding(padding: const EdgeInsets.all(16), child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                Text('Current Sale', style: Theme.of(context).textTheme.headlineSmall),
                const SizedBox(height: 8),
                Expanded(child: _cart.isEmpty ? const Center(child: Text('Scan or select products')) : ListView(children: [for (final line in _cart.values) ListTile(title: Text(line.name), subtitle: Text('Rs. ${line.price.toStringAsFixed(0)} × ${line.quantity}'), trailing: Row(mainAxisSize: MainAxisSize.min, children: [IconButton(onPressed: () => setState(() { if (line.quantity > 1) { line.quantity--; } else { _cart.remove(line.id); } }), icon: const Icon(Icons.remove)), IconButton(onPressed: () => _add(_products.firstWhere((p) => (p['id'] as num).toInt() == line.id)), icon: const Icon(Icons.add))]))])),
                DropdownButtonFormField<String>(value: _orderType, items: const ['Dine In', 'Takeaway', 'Delivery'].map((x) => DropdownMenuItem(value: x, child: Text(x))).toList(), onChanged: (x) => setState(() { _orderType = x!; if (_orderType != 'Dine In') _tableId = null; }), decoration: const InputDecoration(labelText: 'Order type')),
                if (_orderType == 'Dine In') DropdownButtonFormField<int>(value: _tableId, items: _tables.map((t) => DropdownMenuItem(value: (t['id'] as num).toInt(), child: Text('${t['name']}'))).toList(), onChanged: (x) => setState(() => _tableId = x), decoration: const InputDecoration(labelText: 'Table')),
                DropdownButtonFormField<String>(value: _payment, items: const ['Cash', 'Card', 'Online'].map((x) => DropdownMenuItem(value: x, child: Text(x))).toList(), onChanged: (x) => setState(() => _payment = x!), decoration: const InputDecoration(labelText: 'Payment')),
                const SizedBox(height: 12),
                Text('Subtotal: Rs. ${_subtotal.toStringAsFixed(2)}'),
                Text('TOTAL: Rs. ${_subtotal.toStringAsFixed(2)}', style: Theme.of(context).textTheme.titleLarge),
                const SizedBox(height: 12),
                FilledButton.icon(onPressed: _saving ? null : _completeSale, icon: const Icon(Icons.point_of_sale), label: Text(_saving ? 'Processing...' : 'Complete Sale')),
                if (_status.isNotEmpty) Padding(padding: const EdgeInsets.only(top: 8), child: Text(_status)),
              ]))),
            ]),
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
