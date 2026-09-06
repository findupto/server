import 'package:flutter/material.dart';
import '../core/api_client.dart';

class DesktopPosPage extends StatefulWidget {
  final PosApiClient api;
  const DesktopPosPage({super.key, required this.api});

  @override
  State<DesktopPosPage> createState() => _DesktopPosPageState();
}

class _DesktopPosPageState extends State<DesktopPosPage> {
  final search = TextEditingController();
  final barcode = TextEditingController();
  final paymentReference = TextEditingController();
  final cart = <int, Map<String, dynamic>>{};
  List<dynamic> products = [];
  List<dynamic> tables = [];
  String orderType = 'Counter';
  String paymentMethod = 'Cash';
  int? tableId;
  bool loading = true;
  bool checkoutBusy = false;
  String? error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final results = await Future.wait([
        widget.api.products(),
        widget.api.tables(active: true),
      ]);
      products = results[0];
      tables = results[1];
      error = null;
    } catch (e) {
      error = e.toString();
    }
    if (mounted) setState(() => loading = false);
  }

  Iterable<Map<String, dynamic>> get filtered {
    final q = search.text.trim().toLowerCase();
    return products.map((e) => Map<String, dynamic>.from(e)).where((p) {
      if (q.isEmpty) return true;
      return '${p['name']} ${p['barcode'] ?? ''}'.toLowerCase().contains(q);
    });
  }

  void add(Map<String, dynamic> p) {
    final id = (p['id'] as num).toInt();
    setState(() {
      final old = cart[id];
      cart[id] = {
        'productId': id,
        'name': p['name'],
        'price': (p['price'] as num).toDouble(),
        'quantity': (old?['quantity'] ?? 0) + 1,
      };
    });
  }

  void remove(int id) {
    setState(() {
      final item = cart[id];
      if (item == null) return;
      final quantity = item['quantity'] as int;
      if (quantity <= 1) {
        cart.remove(id);
      } else {
        item['quantity'] = quantity - 1;
      }
    });
  }

  double get total => cart.values.fold(
        0,
        (sum, item) => sum + (item['price'] as double) * (item['quantity'] as int),
      );

  Future<void> checkout() async {
    if (checkoutBusy || cart.isEmpty) return;
    if (orderType == 'Dine In' && tableId == null) {
      _show('Select a table for dine-in orders.');
      return;
    }
    final reference = paymentReference.text.trim();
    if ((paymentMethod == 'Card' || paymentMethod == 'Online') && reference.isEmpty) {
      _show('Payment reference is required for card/online payments.');
      return;
    }

    setState(() => checkoutBusy = true);
    final operationId = 'desktop-pos-${DateTime.now().toUtc().microsecondsSinceEpoch}';
    final paymentOperationId = '$operationId-payment';
    try {
      final order = await widget.api.createStaffOrder(
        tableId: tableId,
        items: cart.values
            .map((x) => {'productId': x['productId'], 'quantity': x['quantity']})
            .toList(),
        orderType: orderType,
        clientOperationId: operationId,
      );
      final orderId = (order['id'] as num).toInt();
      await widget.api.collectPayment(
        orderId,
        amountTendered: total,
        method: paymentMethod,
        reference: reference,
        clientOperationId: paymentOperationId,
      );
      try {
        await widget.api.printReceipt(orderId);
      } catch (_) {
        // Printing is best-effort; a successful sale must not be rolled back.
      }
      if (!mounted) return;
      setState(() {
        cart.clear();
        tableId = null;
        paymentReference.clear();
      });
      _show('Sale completed');
    } catch (e) {
      if (mounted) _show('Sale failed: $e');
    } finally {
      if (mounted) setState(() => checkoutBusy = false);
    }
  }

  void _show(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  void dispose() {
    search.dispose();
    barcode.dispose();
    paymentReference.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (loading) return const Scaffold(body: Center(child: CircularProgressIndicator()));
    if (error != null) {
      return Scaffold(
        body: Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(error!, textAlign: TextAlign.center),
              const SizedBox(height: 12),
              FilledButton.icon(
                onPressed: () {
                  setState(() => loading = true);
                  _load();
                },
                icon: const Icon(Icons.refresh),
                label: const Text('Retry'),
              ),
            ],
          ),
        ),
      );
    }

    return Scaffold(
      appBar: AppBar(
        title: const Text('FindUpTo POS'),
        actions: [
          DropdownButton<String>(
            value: orderType,
            items: const ['Counter', 'Dine In', 'Takeaway', 'Delivery']
                .map((x) => DropdownMenuItem(value: x, child: Text(x)))
                .toList(),
            onChanged: checkoutBusy
                ? null
                : (value) => setState(() {
                      orderType = value!;
                      if (orderType != 'Dine In') tableId = null;
                    }),
          ),
          const SizedBox(width: 16),
        ],
      ),
      body: Row(
        children: [
          Expanded(
            flex: 3,
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                children: [
                  TextField(
                    controller: barcode,
                    autofocus: true,
                    decoration: const InputDecoration(
                      labelText: 'Scan barcode',
                      prefixIcon: Icon(Icons.qr_code_scanner),
                    ),
                    onSubmitted: (value) {
                      final code = value.trim();
                      final matches = products
                          .map((e) => Map<String, dynamic>.from(e))
                          .where((p) => p['barcode'] == code);
                      if (matches.isNotEmpty) add(matches.first);
                      barcode.clear();
                    },
                  ),
                  const SizedBox(height: 8),
                  TextField(
                    controller: search,
                    decoration: const InputDecoration(
                      labelText: 'Search products',
                      prefixIcon: Icon(Icons.search),
                    ),
                    onChanged: (_) => setState(() {}),
                  ),
                  const SizedBox(height: 12),
                  Expanded(
                    child: GridView.builder(
                      gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
                        maxCrossAxisExtent: 220,
                        childAspectRatio: 1.7,
                      ),
                      itemCount: filtered.length,
                      itemBuilder: (_, i) {
                        final p = filtered.elementAt(i);
                        return Card(
                          child: InkWell(
                            onTap: checkoutBusy ? null : () => add(p),
                            child: Padding(
                              padding: const EdgeInsets.all(12),
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(p['name'], style: Theme.of(context).textTheme.titleMedium),
                                  const Spacer(),
                                  Text('Rs. ${p['price']}'),
                                  const Align(
                                    alignment: Alignment.bottomRight,
                                    child: Icon(Icons.add_circle_outline),
                                  ),
                                ],
                              ),
                            ),
                          ),
                        );
                      },
                    ),
                  ),
                ],
              ),
            ),
          ),
          SizedBox(
            width: 390,
            child: Card(
              margin: const EdgeInsets.all(12),
              child: Column(
                children: [
                  const ListTile(title: Text('Current Sale'), leading: Icon(Icons.receipt_long)),
                  const Divider(),
                  if (orderType == 'Dine In')
                    Padding(
                      padding: const EdgeInsets.symmetric(horizontal: 16),
                      child: DropdownButtonFormField<int>(
                        value: tableId,
                        decoration: const InputDecoration(labelText: 'Table'),
                        items: tables.map((raw) {
                          final table = Map<String, dynamic>.from(raw);
                          return DropdownMenuItem<int>(
                            value: (table['id'] as num).toInt(),
                            child: Text('${table['name']}'),
                          );
                        }).toList(),
                        onChanged: checkoutBusy ? null : (value) => setState(() => tableId = value),
                      ),
                    ),
                  Expanded(
                    child: ListView(
                      children: cart.values
                          .map(
                            (x) => ListTile(
                              title: Text(x['name']),
                              subtitle: Text('${x['quantity']} × Rs. ${x['price']}'),
                              trailing: Wrap(
                                children: [
                                  IconButton(
                                    onPressed: checkoutBusy ? null : () => remove(x['productId']),
                                    icon: const Icon(Icons.remove),
                                  ),
                                  IconButton(
                                    onPressed: checkoutBusy ? null : () => add(x),
                                    icon: const Icon(Icons.add),
                                  ),
                                ],
                              ),
                            ),
                          )
                          .toList(),
                    ),
                  ),
                  Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      children: [
                        Text('Total  Rs. ${total.toStringAsFixed(2)}', style: Theme.of(context).textTheme.headlineSmall),
                        const SizedBox(height: 8),
                        DropdownButtonFormField<String>(
                          value: paymentMethod,
                          decoration: const InputDecoration(labelText: 'Payment'),
                          items: const ['Cash', 'Card', 'Online']
                              .map((x) => DropdownMenuItem(value: x, child: Text(x)))
                              .toList(),
                          onChanged: checkoutBusy ? null : (v) => setState(() => paymentMethod = v!),
                        ),
                        if (paymentMethod != 'Cash') ...[
                          const SizedBox(height: 8),
                          TextField(
                            controller: paymentReference,
                            decoration: const InputDecoration(labelText: 'Payment reference'),
                          ),
                        ],
                        const SizedBox(height: 12),
                        SizedBox(
                          width: double.infinity,
                          child: FilledButton.icon(
                            onPressed: checkoutBusy ? null : checkout,
                            icon: checkoutBusy
                                ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2))
                                : const Icon(Icons.point_of_sale),
                            label: Text(checkoutBusy ? 'Processing...' : 'Complete Sale'),
                          ),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}
